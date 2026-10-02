namespace MOPAChroma.Scanning;

/// <summary>
/// The result of finding a test grid without any clicks.
/// <para><c>Grid</c> is in the card's own terms (row 0 is the card's top row, column 0 its
/// left column, as the card reads with its labels upright) and in full-resolution image
/// pixels, already refined against the squares.</para>
/// <para><c>TurnDegrees</c> is how far the card is turned clockwise in the image: 0 when the
/// labels read normally. Oxide colours change with the direction light falls on them, so
/// anything other than 0 is worth a warning.</para>
/// </summary>
public sealed record GridDetection(
    GridGeometry Grid,
    int TurnDegrees,
    bool SquaresDarker,
    int SquaresFound,
    double LabelScore,
    bool PlacementCertain,
    double RmsPixels,
    int CellsUsed,
    IReadOnlyList<string> Notes)
{
    /// <summary>Squares found and placed on the lattice, as a share of the whole grid.</summary>
    public double Coverage => SquaresFound / (double)(Grid.Rows * Grid.Columns);

    /// <summary>A quick verdict for the UI: placed, enough squares, and a tight fit.</summary>
    public bool LooksGood => PlacementCertain && CellsUsed >= Math.Max(6, Grid.Rows * Grid.Columns / 10)
                             && RmsPixels < Grid.Pitch * 0.06;
}

/// <summary>
/// Finds a grid of engraved squares in a scan or photo without clicks.
///
/// <list type="number">
/// <item>Work on a reduced grey copy. Subtract a heavily blurred copy so the squares stand
/// out from their local surroundings, whatever the lighting does across the frame.</item>
/// <item>Keep blobs that are square-shaped (by their second moments, so tilt does not matter)
/// and of the dominant size. Labels, specks and the card edge fall out here.</item>
/// <item>Link neighbouring blobs into a lattice, following the local spacing so a perspective
/// photo (squares shrinking with distance) still links up, and fit a homography to it.</item>
/// <item>Decide where the lattice sits on the card and which way up it is. Dim squares may be
/// missing, so the lattice found can be smaller than the card. MakeIt engraves a value
/// label beside every row (left) and under every column (bottom); the placement and
/// rotation that put labels in those positions win.</item>
/// <item>Refine at full resolution with <see cref="GridFit"/>.</item>
/// </list>
/// Both polarities are tried: squares lighter than the bare surface (oxide on polished
/// steel) and darker (most marks on light materials).
/// </summary>
public static class GridDetect
{
    /// <summary>Long side of the reduced working copy, in pixels.</summary>
    public const int DefaultWorkingSize = 1600;

    /// <summary>A window counts as "label ink" above this multiple of the squares' own texture.</summary>
    public const double LabelInkRatio = 2.2;

    private static readonly AsyncLocal<Action<string>?> trace = new();
    /// <summary>Diagnostic hook for tests: one line per detection stage. Per call flow, so parallel tests don't share it.</summary>
    internal static Action<string>? Trace { get => trace.Value; set => trace.Value = value; }

    /// <param name="img">The scan or photo, at full resolution.</param>
    /// <param name="rows">Rows on the card if known (the card definition), else found from the image.</param>
    /// <param name="columns">Columns on the card if known.</param>
    /// <param name="workingSize">Long side of the reduced copy the search runs on; the final fit uses full resolution.</param>
    /// <param name="marks">What the card carries beside its grid to show which way up it is: MakeIt's value
    /// labels (the default) or Atlas's corner crosses.</param>
    public static GridDetection? Detect(RgbImage img, int? rows = null, int? columns = null, int workingSize = DefaultWorkingSize,
                                        CardMarks? marks = null)
    {
        if ((rows is { } r0 && r0 < 1) || (columns is { } c0 && c0 < 1))
            throw new ArgumentException("Rows and columns must be positive when given.");

        var (work, grad, best) = Search(img, workingSize);
        if (best is null) return null;
        return Finish(img, work, grad, best, rows, columns, marks);
    }

    /// <summary>
    /// Finds every separate grid of the given shape in one picture, such as a scanned sheet
    /// with several cards on it. Grids that happen to line up share one lattice of squares;
    /// this splits it into islands (two or more empty positions apart) and places each one.
    /// Islands that cannot be placed are left out.
    /// </summary>
    public static IReadOnlyList<GridDetection> DetectEach(RgbImage img, int rows, int columns, CardMarks marks,
                                                         int workingSize = DefaultWorkingSize)
    {
        var (work, grad, candidates) = SearchAll(img, workingSize);
        // A sheet can hold cards of several sizes (a MakeIt test beside Atlas grids). Each size
        // gives its own lattice; the first whose islands place with certainty wins.
        List<GridDetection>? first = null;
        foreach (var lat in candidates)
        {
            var list = new List<GridDetection>();
            foreach (var part in Islands(lat).Where(p => p.Nodes.Count >= 6))
                if (Finish(img, work, grad, part, rows, columns, marks) is { } d) list.Add(d);
            if (list.Any(d => d.PlacementCertain)) return list;
            first ??= list;
        }
        return first ?? [];
    }

    /// <summary>Splits a lattice into groups of nodes no more than two positions apart, each with its own fit.</summary>
    private static IEnumerable<Lattice> Islands(Lattice lat)
    {
        var left = new HashSet<int>(Enumerable.Range(0, lat.Nodes.Count));
        while (left.Count > 0)
        {
            int seed = left.First();
            left.Remove(seed);
            var group = new List<int> { seed };
            for (int k = 0; k < group.Count; k++)
            {
                var a = lat.Nodes[group[k]];
                foreach (int o in left.Where(o => Math.Abs(lat.Nodes[o].I - a.I) <= 2 && Math.Abs(lat.Nodes[o].J - a.J) <= 2).ToList())
                {
                    left.Remove(o);
                    group.Add(o);
                }
            }
            // Each island gets its own fit: separate cards are never exactly on one lattice, and
            // a fit shared across a sheet is off at every card by the cards' own misalignment.
            var nodes = group.Select(g => lat.Nodes[g]).ToList();
            yield return new Lattice { Nodes = nodes, H = (nodes.Count >= 6 ? FitLattice(nodes) : null) ?? lat.H, Darker = lat.Darker };
        }
    }

    private static (Working Work, Gradient Grad, Lattice? Best) Search(RgbImage img, int workingSize)
    {
        var (work, grad, all) = SearchAll(img, workingSize);
        return (work, grad, all.FirstOrDefault());
    }

    /// <summary>
    /// Every lattice worth trying, largest first: for each polarity and contrast step, one from
    /// the dominant square size and one per distinct square size (a sheet with cards of
    /// different sizes), with near-duplicates dropped.
    /// </summary>
    private static (Working Work, Gradient Grad, List<Lattice> Lattices) SearchAll(RgbImage img, int workingSize)
    {
        var work = Working.From(img, workingSize);
        var dif = work.LocalContrast();
        var grad = work.Gradient();

        var found = new List<Lattice>();
        foreach (bool darker in new[] { false, true })
        foreach (float thr in ContrastSteps)
        {
            var all = FindSquareBlobs(work, dif, darker, thr);
            var dominant = DominantSize(all);
            Trace?.Invoke($"darker={darker} thr={thr}: {dominant.Count} square blobs of {all.Count}");
            foreach (var blobs in new[] { dominant }.Concat(SizeGroups(all)))
            {
                if (blobs.Count < 6) continue;
                var lat = BuildLattice(blobs, all, darker);
                Trace?.Invoke($"darker={darker}: {blobs.Count} blobs → lattice {(lat is null ? "none" : $"{lat.Nodes.Count} nodes, i {lat.IMin}..{lat.IMax}, j {lat.JMin}..{lat.JMax}")}");
                if (lat is not null) found.Add(lat);
            }
        }
        // Keep the largest of lattices that cover mostly the same squares.
        var kept = new List<Lattice>();
        foreach (var lat in found.OrderByDescending(l => l.Nodes.Count))
        {
            var pts = lat.Nodes.Select(n => n.P).ToList();
            bool dup = kept.Any(k =>
            {
                double tol = 0.25 * (k.H.Map(1, 0) - k.H.Map(0, 0)).Length;
                int shared = pts.Count(p => k.Nodes.Any(n => (n.P - p).Length < tol));
                return k.Darker == lat.Darker && shared >= 0.6 * pts.Count;
            });
            if (!dup) kept.Add(lat);
        }
        return (work, grad, kept);
    }

    /// <summary>
    /// Square blobs split by size into groups no wider than about ±12 %, the groups covering
    /// the most area first. A flat scan keeps one card's squares within that; different cards
    /// on one sheet (a MakeIt colour test, Atlas grids of another cell size) fall into separate groups.
    /// </summary>
    private static IEnumerable<List<Blob>> SizeGroups(List<Blob> blobs)
    {
        const double band = 0.35;
        var left = blobs.OrderBy(b => b.Area).ToList();
        for (int g = 0; g < 3 && left.Count >= 6; g++)
        {
            var logs = left.Select(b => Math.Log2(Math.Sqrt(b.Area))).ToArray();
            double bestArea = 0, area = 0; int bi = -1, bj = -1;
            for (int i = 0, j = 0; i < logs.Length; i++)
            {
                while (j < logs.Length && logs[j] - logs[i] <= band) area += left[j++].Area;
                if (j - i >= 6 && area > bestArea) { bestArea = area; bi = i; bj = j; }
                area -= left[i].Area;
            }
            if (bi < 0) yield break;
            yield return left.GetRange(bi, bj - bi);
            left.RemoveRange(bi, bj - bi);
        }
    }

    private static GridDetection? Finish(RgbImage img, Working work, Gradient grad, Lattice best, int? rows, int? columns, CardMarks? marks)
    {
        var placement = Place(best, work, grad, rows, columns, marks ?? CardMarks.MakeItLabels);
        Trace?.Invoke(placement is null ? "no placement" : $"placement turn {placement.TurnDegrees} score {placement.Score:0.00} certain {placement.Certain}");
        if (placement is null) return null;

        // An Atlas card's grid number (a dot code with a check) settles what the corners leave
        // open, such as which column the grid starts at when its first columns left no mark:
        // only the true placement reads as a valid number.
        if (!placement.Certain && marks is CornerCrossMarks { Layout: { } layout } && placement.Rivals.Count > 1)
        {
            var reads = placement.Rivals
                .Where(r => r.Rows == layout.Rows && r.Columns == layout.Columns)
                .Select(r => (r, n: Grids.CardId.Read(img, GridGeometry.FromWarp(r.CardToLattice.Then(best.H).ScaleOutput(1 / work.Scale), r.Rows, r.Columns), layout)))
                .Where(x => x.n is not null).ToList();
            Trace?.Invoke($"grid number check: {reads.Count} of {placement.Rivals.Count} placements read a number");
            if (reads.Count == 1)
            {
                var idNotes = reads[0].r.Notes.Where(n => !n.StartsWith("The labels do not settle")).ToList();
                if (reads[0].r.TurnDegrees != 0 && !idNotes.Any(n => n.StartsWith("The card is")))
                    idNotes.Add($"The card is {DescribeTurn(reads[0].r.TurnDegrees)} from the standard orientation (labels readable).");
                idNotes.Add($"Placed by its grid number ({reads[0].n}).");
                placement = reads[0].r with { Certain = true, Notes = idNotes };
            }
        }

        // Card (column, row) → lattice → working pixels → full-resolution pixels.
        var cardWarp = placement.CardToLattice.Then(best.H).ScaleOutput(1 / work.Scale);
        var start = GridGeometry.FromWarp(cardWarp, placement.Rows, placement.Columns);
        var fit = GridFit.Refine(img, start, squaresDarker: best.Darker);
        // The lattice placed the grid to within a fraction of a cell. If refining moved it much
        // further, blank, light or shadowed cells pulled it off (titanium): refine again,
        // counting only cells that look like squares.
        bool strict = false;
        if (MaxCornerShift(start, fit.Grid) > 0.25 * start.PitchAt(0, 0))
        {
            var careful = GridFit.Refine(img, start, squaresDarker: best.Darker, strict: true);
            Trace?.Invoke($"refine moved the grid {MaxCornerShift(start, fit.Grid):0.0}px; careful refine moves it {MaxCornerShift(start, careful.Grid):0.0}px");
            if (careful.CellsUsed >= 6 && MaxCornerShift(start, careful.Grid) < MaxCornerShift(start, fit.Grid)) { fit = careful; strict = true; }
        }

        // A scan is affine to within a fraction of a pixel; keep the simpler model then, so
        // nothing downstream has to care. Only a real photo keeps its perspective term.
        var grid = fit.Grid;
        var affine = GridFit.Refine(img, new GridGeometry(grid.Origin, grid.ColStep, grid.RowStep, grid.Rows, grid.Columns),
                                    squaresDarker: best.Darker, strict: strict);
        if (affine.CellsUsed >= fit.CellsUsed * 0.95 && affine.RmsPixels <= fit.RmsPixels * 1.15 + 0.15)
            fit = affine;

        var notes = new List<string>(placement.Notes);
        if (fit.Grid.IsPerspective) notes.Add("Photographed at an angle: perspective corrected.");
        return new GridDetection(fit.Grid, placement.TurnDegrees, best.Darker, best.Nodes.Count,
                                 placement.Score, placement.Certain, fit.RmsPixels, fit.CellsUsed, notes);
    }

    private static double MaxCornerShift(GridGeometry a, GridGeometry b) =>
        new[] { (0, 0), (0, a.Columns - 1), (a.Rows - 1, 0), (a.Rows - 1, a.Columns - 1) }
            .Max(rc => (a.CentreAt(rc.Item1, rc.Item2) - b.CentreAt(rc.Item1, rc.Item2)).Length);

    // ------------------------------------------------------------------ working image

    private sealed class Working
    {
        public required int W { get; init; }
        public required int H { get; init; }
        public required float[] Gray { get; init; }
        /// <summary>Working pixels per full-resolution pixel (≤ 1).</summary>
        public required double Scale { get; init; }

        public static Working From(RgbImage img, int size)
        {
            double k = Math.Min(1.0, size / (double)Math.Max(img.Width, img.Height));
            int w = Math.Max(1, (int)Math.Round(img.Width * k)), h = Math.Max(1, (int)Math.Round(img.Height * k));
            var g = new float[w * h];
            // Box-average every full-resolution pixel into its working pixel.
            var cnt = new int[w * h];
            for (int y = 0; y < img.Height; y++)
            {
                int wy = Math.Min(h - 1, (int)(y * k));
                int row = y * img.Width * 3;
                for (int x = 0; x < img.Width; x++)
                {
                    int i = row + x * 3, wi = wy * w + Math.Min(w - 1, (int)(x * k));
                    g[wi] += 0.299f * img.Pixels[i] + 0.587f * img.Pixels[i + 1] + 0.114f * img.Pixels[i + 2];
                    cnt[wi]++;
                }
            }
            for (int i = 0; i < g.Length; i++) if (cnt[i] > 0) g[i] /= cnt[i];
            return new Working { W = w, H = h, Gray = g, Scale = w / (double)img.Width };
        }

        /// <summary>Grey minus a wide box blur: positive where locally light, negative where locally dark.</summary>
        public float[] LocalContrast()
        {
            int r = Math.Max(6, Math.Max(W, H) / 24);
            var integral = new double[(W + 1) * (H + 1)];
            for (int y = 0; y < H; y++)
            {
                double rowSum = 0;
                for (int x = 0; x < W; x++)
                {
                    rowSum += Gray[y * W + x];
                    integral[(y + 1) * (W + 1) + x + 1] = integral[y * (W + 1) + x + 1] + rowSum;
                }
            }
            var d = new float[W * H];
            for (int y = 0; y < H; y++)
            {
                int y0 = Math.Max(0, y - r), y1 = Math.Min(H, y + r + 1);
                for (int x = 0; x < W; x++)
                {
                    int x0 = Math.Max(0, x - r), x1 = Math.Min(W, x + r + 1);
                    double s = integral[y1 * (W + 1) + x1] - integral[y0 * (W + 1) + x1] - integral[y1 * (W + 1) + x0] + integral[y0 * (W + 1) + x0];
                    d[y * W + x] = Gray[y * W + x] - (float)(s / ((x1 - x0) * (y1 - y0)));
                }
            }
            return d;
        }

        /// <summary>Sobel gradient (x and y): high on edges and text strokes, low inside even areas.</summary>
        public Gradient Gradient()
        {
            var gxs = new float[W * H];
            var gys = new float[W * H];
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    float a = Gray[(y - 1) * W + x - 1], b = Gray[(y - 1) * W + x], c = Gray[(y - 1) * W + x + 1];
                    float d = Gray[y * W + x - 1], f = Gray[y * W + x + 1];
                    float g = Gray[(y + 1) * W + x - 1], h = Gray[(y + 1) * W + x], i = Gray[(y + 1) * W + x + 1];
                    gxs[y * W + x] = (c + 2 * f + i) - (a + 2 * d + g);
                    gys[y * W + x] = (g + 2 * h + i) - (a + 2 * b + c);
                }
            return new Gradient(gxs, gys, W, H);
        }
    }

    /// <summary>
    /// Gradient field with the two window measures placement needs: how much edge there is
    /// (ink) and whether it all runs one way (a straight edge, such as the card's own
    /// outline, rather than text).
    /// </summary>
    private sealed record Gradient(float[] Gx, float[] Gy, int W, int H)
    {
        /// <summary>Mean gradient magnitude and orientation coherence (0 = every direction, 1 = one straight edge).</summary>
        public (double ink, double coherence) Window(PointD c, double half)
        {
            int x0 = (int)Math.Round(c.X - half), x1 = (int)Math.Round(c.X + half);
            int y0 = (int)Math.Round(c.Y - half), y1 = (int)Math.Round(c.Y + half);
            if (x0 < 1 || y0 < 1 || x1 >= W - 1 || y1 >= H - 1 || x1 <= x0 || y1 <= y0) return (double.NaN, double.NaN);
            double mag = 0, jxx = 0, jyy = 0, jxy = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double gx = Gx[y * W + x], gy = Gy[y * W + x];
                    mag += Math.Sqrt(gx * gx + gy * gy);
                    jxx += gx * gx; jyy += gy * gy; jxy += gx * gy;
                }
            double tr = jxx + jyy;
            double coh = tr < 1e-9 ? 0 : Math.Sqrt((jxx - jyy) * (jxx - jyy) + 4 * jxy * jxy) / tr;
            return (mag / ((x1 - x0 + 1) * (y1 - y0 + 1)), coh);
        }
    }

    // ------------------------------------------------------------------ square-shaped blobs

    private sealed record Blob(PointD Centre, double Area);

    /// <summary>
    /// Grey levels a square must stand above (or below) its surroundings. No single value
    /// suits both a faint mark on dark steel and a bold one under uneven light, so a few are
    /// tried and the one that yields the largest lattice wins.
    /// </summary>
    private static readonly float[] ContrastSteps = [5f, 10f, 20f, 35f];

    private static List<Blob> FindSquareBlobs(Working w, float[] dif, bool darker, float thr)
    {
        var mask = new bool[dif.Length];
        for (int i = 0; i < dif.Length; i++) mask[i] = darker ? dif[i] < -thr : dif[i] > thr;

        var blobs = new List<Blob>();
        var stack = new Stack<int>();
        int minArea = 30, maxArea = w.W * w.H / 50; // squares under ~5 px across cannot be measured anyway
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start]) continue;
            mask[start] = false;
            stack.Push(start);
            long n = 0; double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
            while (stack.Count > 0)
            {
                int p = stack.Pop(), x = p % w.W, y = p / w.W;
                n++; sx += x; sy += y; sxx += (double)x * x; syy += (double)y * y; sxy += (double)x * y;
                if (x > 0 && mask[p - 1]) { mask[p - 1] = false; stack.Push(p - 1); }
                if (x < w.W - 1 && mask[p + 1]) { mask[p + 1] = false; stack.Push(p + 1); }
                if (y > 0 && mask[p - w.W]) { mask[p - w.W] = false; stack.Push(p - w.W); }
                if (y < w.H - 1 && mask[p + w.W]) { mask[p + w.W] = false; stack.Push(p + w.W); }
            }
            if (n < minArea || n > maxArea) continue;
            double mx = sx / n, my = sy / n;
            double vxx = sxx / n - mx * mx + 1 / 12.0, vyy = syy / n - my * my + 1 / 12.0, vxy = sxy / n - mx * my;
            double tr = vxx + vyy, det = vxx * vyy - vxy * vxy;
            if (det <= 0) continue;
            double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
            double l1 = tr / 2 + disc, l2 = tr / 2 - disc;
            // A filled square (any rotation) has equal second moments and area = 12·sqrt(det).
            // Text, rings and partial blobs fail one or both.
            double elong = l1 / Math.Max(l2, 1e-9), solidity = n / (12 * Math.Sqrt(det));
            if (elong > 1.7 || solidity < 0.8 || solidity > 1.2) continue;
            blobs.Add(new Blob(new PointD(mx + 0.5, my + 0.5), n));
        }
        return blobs;
    }

    /// <summary>
    /// Keeps the blobs in the size band that covers the most area. Squares on one card share
    /// a size, and they outweigh dust and specks by area even when outnumbered by them.
    /// </summary>
    private static List<Blob> DominantSize(List<Blob> blobs)
    {
        if (blobs.Count < 6) return blobs;
        var sorted = blobs.OrderBy(b => b.Area).ToArray();
        var logs = sorted.Select(b => Math.Log2(Math.Sqrt(b.Area))).ToArray();
        const double band = 0.8; // a factor of ~1.74 in side length: enough for a steep photo
        double bestArea = 0, bestLo = logs[0], area = 0;
        for (int i = 0, j = 0; i < logs.Length; i++)
        {
            while (j < logs.Length && logs[j] - logs[i] <= band) area += sorted[j++].Area;
            if (j - i >= 6 && area > bestArea) { bestArea = area; bestLo = logs[i]; }
            area -= sorted[i].Area;
        }
        return blobs.Where(b => { double v = Math.Log2(Math.Sqrt(b.Area)); return v >= bestLo - 1e-9 && v <= bestLo + band + 1e-9; }).ToList();
    }

    // ------------------------------------------------------------------ lattice

    private sealed class Lattice
    {
        public required List<(int I, int J, PointD P)> Nodes { get; init; }
        /// <summary>Lattice (i, j) → working pixels.</summary>
        public required Homography H { get; init; }
        public required bool Darker { get; init; }
        public int IMin => Nodes.Min(n => n.I);
        public int IMax => Nodes.Max(n => n.I);
        public int JMin => Nodes.Min(n => n.J);
        public int JMax => Nodes.Max(n => n.J);
    }

    /// <param name="blobs">Square blobs of the dominant size: these build the lattice.</param>
    /// <param name="all">Every square-shaped blob. Once the lattice is known, any of these that
    /// sits on a lattice point joins it, whatever its size (far squares in a steep photo).</param>
    /// <param name="darker">Whether these blobs are darker than their surroundings.</param>
    private static Lattice? BuildLattice(List<Blob> blobs, List<Blob> all, bool darker)
    {
        double side = Median(blobs.Select(b => Math.Sqrt(b.Area)));
        double rMax = 3.4 * side;
        var hash = new SpatialHash(blobs.Select(b => b.Centre).ToList(), rMax);

        // 1. Typical neighbour vectors: the up-to-4 nearest neighbours of every blob, at least
        //    a square's width away (they cannot overlap) and at most ~2 squares away.
        var vecs = new List<PointD>();
        for (int k = 0; k < blobs.Count; k++)
        {
            var p = blobs[k].Centre;
            foreach (var q in hash.Near(p, 2.6 * side).Where(q => q != k)
                                  .Select(q => blobs[q].Centre - p).Where(d => d.Length > 1.02 * side)
                                  .OrderBy(d => d.Length).Take(4))
                vecs.Add(q);
        }
        Trace?.Invoke($"side {side:0.0}px, {vecs.Count} neighbour vectors");
        if (vecs.Count < 8) return null;

        var (a1, a2) = TwoDirections(vecs);
        Trace?.Invoke($"directions {a1 * 180 / Math.PI:0.0}° {a2 * 180 / Math.PI:0.0}°");
        if (a1 is null || a2 is null) return null;
        // u: the more horizontal direction, pointing right; v: the other, pointing down.
        var (u, v) = Math.Abs(Math.Cos(a1.Value)) >= Math.Abs(Math.Cos(a2.Value)) ? (a1.Value, a2.Value) : (a2.Value, a1.Value);
        var du = new PointD(Math.Cos(u), Math.Sin(u)); if (du.X < 0) du = du * -1;
        var dv = new PointD(Math.Cos(v), Math.Sin(v)); if (dv.Y < 0) dv = dv * -1;
        double pu = Median(vecs.Where(d => AngleTo(d, du) < 0.2).Select(d => d.Length));
        double pv = Median(vecs.Where(d => AngleTo(d, dv) < 0.2).Select(d => d.Length));
        Trace?.Invoke($"pitch u {pu:0.0} v {pv:0.0}");
        if (double.IsNaN(pu) || double.IsNaN(pv)) return null;

        // 2. Breadth-first walk from a well-connected blob near the middle, giving each blob
        //    integer lattice coordinates. Each blob carries its own local step vectors, so the
        //    walk follows a perspective photo's gradually changing spacing. On a sheet with
        //    several cards of different spacing (a MakeIt test beside Atlas grids) the first
        //    seed may sit on a card the spacing does not fit, so a few seeds are tried and the
        //    largest walk is kept.
        var centroid = new PointD(blobs.Average(b => b.Centre.X), blobs.Average(b => b.Centre.Y));
        var seeds = Enumerable.Range(0, blobs.Count)
            .OrderByDescending(k => hash.Near(blobs[k].Centre, 1.6 * Math.Max(pu, pv)).Count())
            .ThenBy(k => (blobs[k].Centre - centroid).Length).ToList();
        List<(int i, int j, PointD p)>? bestWalk = null;
        var tried = new bool[blobs.Count];
        int walks = 0;
        foreach (int seed in seeds)
        {
            if (tried[seed]) continue;
            if (walks++ >= 8 || (bestWalk is not null && bestWalk.Count * 2 >= blobs.Count)) break;
            var walk = Walk(seed);
            foreach (var w in walk.Members) tried[w] = true;
            if (bestWalk is null || walk.Nodes.Count > bestWalk.Count) bestWalk = walk.Nodes;
        }
        var nodes = bestWalk ?? [];
        Trace?.Invoke($"walk assigned {nodes.Count} of {blobs.Count} ({walks} seeds)");
        if (nodes.Count < 6) return null;


        // 3. Fit lattice → pixels, drop what disagrees, then sweep in blobs the walk missed.
        //    A swept-in blob must also be about the size a square should be at that spot.
        double sideToPitch = side / Math.Sqrt(pu * pv);
        Homography? h = null;
        for (int pass = 0; pass < 3; pass++)
        {
            h = FitLattice(nodes);
            if (h is null) return null;
            var hh = h;
            double tol = 0.3 * Math.Min(pu, pv);
            nodes = nodes.Where(n => (hh.Map(n.Item1, n.Item2) - n.Item3).Length <= tol).ToList();
            if (nodes.Count < 6) return null;
            h = FitLattice(nodes);
            if (h is null || h.Inverse() is not { } inv) return null;
            var taken = nodes.Select(n => (n.Item1, n.Item2)).ToHashSet();
            var used = nodes.Select(n => n.Item3).ToHashSet();
            foreach (var b in all)
            {
                if (used.Contains(b.Centre)) continue;
                var g = inv.Map(b.Centre);
                int gi = (int)Math.Round(g.X), gj = (int)Math.Round(g.Y);
                if (Math.Abs(g.X - gi) > 0.25 || Math.Abs(g.Y - gj) > 0.25) continue;
                double expect = sideToPitch * LocalPitch(h, gi, gj), got = Math.Sqrt(b.Area);
                if (got < 0.7 * expect || got > 1.35 * expect) continue;
                if (taken.Add((gi, gj))) nodes.Add((gi, gj, b.Centre));
            }
        }
        h = FitLattice(nodes);
        if (h is null) return null;
        return new Lattice { Nodes = nodes, H = h, Darker = darker };

        (List<(int i, int j, PointD p)> Nodes, List<int> Members) Walk(int seed)
        {
            var at = new (int i, int j)?[blobs.Count];
            var stepU = new PointD[blobs.Count];
            var stepV = new PointD[blobs.Count];
            var occupied = new Dictionary<(int, int), int>();
            var queue = new Queue<int>();
            at[seed] = (0, 0); occupied[(0, 0)] = seed;
            stepU[seed] = du * pu; stepV[seed] = dv * pv;
            queue.Enqueue(seed);
            double reach = 3.3 * Math.Max(pu, pv);
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                var (ni, nj) = at[n]!.Value;
                PointD lu = stepU[n], lv = stepV[n];
                double det = lu.X * lv.Y - lu.Y * lv.X;
                if (Math.Abs(det) < 1e-9) continue;
                foreach (int m in hash.Near(blobs[n].Centre, reach))
                {
                    if (m == n || at[m] is not null) continue;
                    var d = blobs[m].Centre - blobs[n].Centre;
                    double a = (d.X * lv.Y - d.Y * lv.X) / det, b = (lu.X * d.Y - lu.Y * d.X) / det;
                    int ai = (int)Math.Round(a), bi = (int)Math.Round(b);
                    if ((ai == 0 && bi == 0) || Math.Abs(ai) + Math.Abs(bi) > 3) continue;
                    if (Math.Abs(a - ai) > 0.22 || Math.Abs(b - bi) > 0.22) continue;
                    var key = (ni + ai, nj + bi);
                    if (occupied.ContainsKey(key)) continue;
                    at[m] = key; occupied[key] = m;
                    // Adopt the observed spacing along a single step; keep the parent's otherwise.
                    stepU[m] = ai != 0 && bi == 0 ? Blend(lu, d * (1.0 / ai)) : lu;
                    stepV[m] = bi != 0 && ai == 0 ? Blend(lv, d * (1.0 / bi)) : lv;
                    queue.Enqueue(m);
                }
            }
            var members = Enumerable.Range(0, blobs.Count).Where(k => at[k] is not null).ToList();
            return (members.Select(k => (at[k]!.Value.i, at[k]!.Value.j, blobs[k].Centre)).ToList(), members);
        }
    }

    private static Homography? FitLattice(List<(int i, int j, PointD p)> nodes)
    {
        var src = nodes.Select(n => new PointD(n.i, n.j)).ToList();
        var dst = nodes.Select(n => n.p).ToList();
        bool spread = nodes.Select(n => n.i).Distinct().Count() >= 3 && nodes.Select(n => n.j).Distinct().Count() >= 3;
        return (nodes.Count >= 8 && spread ? Homography.Fit(src, dst) : null) ?? Homography.Fit(src, dst, affineOnly: true);
    }

    private static PointD Blend(PointD a, PointD b) => a * 0.5 + b * 0.5;

    private static double AngleTo(PointD d, PointD unit)
    {
        double c = Math.Abs((d.X * unit.X + d.Y * unit.Y) / Math.Max(d.Length, 1e-9));
        return Math.Acos(Math.Min(1, c));
    }

    /// <summary>The two dominant directions (radians, mod π) among the neighbour vectors.</summary>
    private static (double?, double?) TwoDirections(List<PointD> vecs)
    {
        const int bins = 90; // 2° each
        var hist = new double[bins];
        foreach (var d in vecs)
        {
            double a = Math.Atan2(d.Y, d.X);
            if (a < 0) a += Math.PI;
            hist[(int)(a / Math.PI * bins) % bins] += 1;
        }
        var smooth = new double[bins];
        for (int i = 0; i < bins; i++)
            smooth[i] = hist[(i + bins - 1) % bins] + 2 * hist[i] + hist[(i + 1) % bins];
        int p1 = Array.IndexOf(smooth, smooth.Max());
        int p2 = -1;
        for (int i = 0; i < bins; i++)
        {
            int dist = Math.Min(Math.Abs(i - p1), bins - Math.Abs(i - p1));
            if (dist < 15) continue; // at least 30° apart: a steep photo skews the lattice, not that much
            if (p2 < 0 || smooth[i] > smooth[p2]) p2 = i;
        }
        if (p2 < 0 || smooth[p2] < Math.Max(3, smooth[p1] * 0.15)) return (null, null);
        return (Refine(p1), Refine(p2));

        double? Refine(int peak)
        {
            // Mean angle of the vectors within ±4° of the peak bin, handling wrap-around.
            double centre = (peak + 0.5) * Math.PI / bins, sx = 0, sy = 0;
            foreach (var d in vecs)
            {
                double a = Math.Atan2(d.Y, d.X);
                double diff = Math.IEEERemainder(a - centre, Math.PI);
                if (Math.Abs(diff) > 4 * Math.PI / 180) continue;
                sx += Math.Cos(2 * (centre + diff)); sy += Math.Sin(2 * (centre + diff));
            }
            return sx == 0 && sy == 0 ? centre : Math.Atan2(sy, sx) / 2;
        }
    }

    // ------------------------------------------------------------------ placement on the card

    private sealed record Placement(Homography CardToLattice, int Rows, int Columns, int TurnDegrees,
                                    double Score, bool Certain, IReadOnlyList<string> Notes)
    {
        /// <summary>When the marks leave the placement open: every placement the marks support (best first).</summary>
        public IReadOnlyList<Placement> Rivals { get; init; } = [];
    }

    /// <summary>
    /// Card (column, row) → lattice (i, j) for a card turned clockwise by <paramref name="turn"/>.
    /// The lattice has i increasing to the right of the image and j downwards.
    /// </summary>
    private static (int ci, int ri, int cj, int rj) TurnMatrix(int turn) => turn switch
    {
        0 => (1, 0, 0, 1),     // i = c,   j = r
        90 => (0, -1, 1, 0),   // i = -r,  j = c   (card's top edge on the image's right)
        180 => (-1, 0, 0, -1), // i = -c,  j = -r
        _ => (0, 1, -1, 0),    // i = r,   j = -c  (card's top edge on the image's left)
    };

    /// <summary>Above this coherence a window's edges all run one way: the card outline or a scratch, not text.</summary>
    private const double StraightEdgeCoherence = 0.8;

    private static Placement? Place(Lattice lat, Working work, Gradient grad, int? rows, int? columns, CardMarks marks)
    {
        // Texture level of the squares themselves: the yardstick for "is there text here".
        double pitch = LocalPitch(lat.H, (lat.IMin + lat.IMax) / 2.0, (lat.JMin + lat.JMax) / 2.0);
        var interior = lat.Nodes.Select(n => grad.Window(n.P, 0.22 * pitch).ink).Where(v => !double.IsNaN(v)).ToList();
        var detected = lat.Nodes.Select(n => (n.I, n.J)).ToHashSet();
        // Floored so a noise-free image (or a very smooth scan) does not turn faint square
        // edges into "text".
        double baseInk = Math.Max(4.0, interior.Count > 0 ? Median(interior) : 4.0);

        var candidates = new List<Placement>();
        foreach (int turn in new[] { 0, 90, 180, 270 })
        {
            var (ci, ri, cj, rj) = TurnMatrix(turn);
            // Lattice → card: M = [[ci, ri], [cj, rj]] is orthogonal with entries ±1, so its
            // inverse is its transpose: c = ci·i + cj·j, r = ri·i + rj·j.
            var card = lat.Nodes.Select(n => (c: ci * n.I + cj * n.J, r: ri * n.I + rj * n.J)).ToList();
            int cMin = card.Min(p => p.c), cMax = card.Max(p => p.c), rMin = card.Min(p => p.r), rMax = card.Max(p => p.r);
            int spanC = cMax - cMin + 1, spanR = rMax - rMin + 1;
            int nC = columns ?? spanC, nR = rows ?? spanR;
            if (spanC > nC || spanR > nR) continue; // more squares than the card has this way round

            for (int kc = 0; kc <= nC - spanC; kc++)
                for (int kr = 0; kr <= nR - spanR; kr++)
                {
                    // card c' = c − cMin + kc  ⇒  c = c' + cMin − kc; then lattice = M · (c, r).
                    int oc = cMin - kc, or = rMin - kr;
                    // i = ci·c + ri·r, j = cj·c + rj·r, with c = c' + oc and r = r' + or.
                    var toLattice = Homography.Affine(
                        new PointD(ci * oc + ri * or, cj * oc + rj * or),
                        new PointD(ci, cj), new PointD(ri, rj));
                    var toWork = toLattice.Then(lat.H);
                    // Windows sit on whole cell positions one step outside the grid: where a square
                    // would be, the window is inside it (no edges), so only text registers.
                    double left, bottom, right;
                    if (marks is CornerCrossMarks cm)
                    {
                        // Crosses above the top-left and top-right cells and below the bottom-left
                        // one; the bottom-right must be empty, or the card is the other way round.
                        double o = cm.OffsetPitches, h = Math.Max(cm.HalfSizePitches * 1.2, 0.12);
                        left = LabelFraction(grad, toWork, baseInk, [(0, -o), (nC - 1, -o)], h);
                        bottom = LabelFraction(grad, toWork, baseInk, [(0, nR - 1 + o)], h);
                        right = LabelFraction(grad, toWork, baseInk, [(nC - 1, nR - 1 + o)], h);
                    }
                    else
                    {
                        left = LabelFraction(grad, toWork, baseInk, Enumerable.Range(0, nR).Select(r => (c: -1.0, r: (double)r)));
                        bottom = LabelFraction(grad, toWork, baseInk, Enumerable.Range(0, nC).Select(c => (c: (double)c, r: (double)nR)));
                        right = LabelFraction(grad, toWork, baseInk, Enumerable.Range(0, nR).Select(r => (c: (double)nC, r: (double)r)));
                    }
                    // Cells of this placement where no square was found: a wrong shift drags the
                    // label column or row onto them, and text inside the grid gives it away.
                    var empty = new List<(double c, double r)>();
                    for (int r = 0; r < nR; r++)
                        for (int c = 0; c < nC; c++)
                        {
                            var l = toLattice.Map(c, r);
                            if (!detected.Contains(((int)Math.Round(l.X), (int)Math.Round(l.Y)))) empty.Add((c, r));
                        }
                    double inside = empty.Count == 0 ? 0 : LabelFraction(grad, toWork, baseInk, empty);
                    double score = left + bottom - 0.5 * right - inside;
                    if (left + bottom > 0.8) Trace?.Invoke($"  turn {turn} kc {kc} kr {kr}: left {left:0.00} bottom {bottom:0.00} right {right:0.00} inside {inside:0.00} of {empty.Count}");
                    candidates.Add(new Placement(toLattice, nR, nC, turn, score, false, []));
                }
        }
        if (candidates.Count == 0) return null;

        var ranked = candidates.OrderByDescending(p => p.Score).ThenBy(p => p.TurnDegrees == 0 ? 0 : 1).ToList();
        foreach (var p in ranked.Take(4)) Trace?.Invoke($"  ranked: turn {p.TurnDegrees} score {p.Score:0.00} origin {p.CardToLattice.Map(0, 0)}");
        var best = ranked[0];
        double runnerUp = ranked.Count > 1 ? ranked[1].Score : double.NegativeInfinity;
        var notes = new List<string>();
        bool labelled = best.Score >= 1.0;
        bool certain = labelled && best.Score - runnerUp >= 0.35;

        if (!labelled)
        {
            // No MakeIt labels to go by (an Atlas-generated card, or labels cut off). Assume
            // the usual way up, and if the squares found do not fill the card, say so.
            var upright = candidates.Where(p => p.TurnDegrees == 0).ToList();
            var pool = upright.Count > 0 ? upright : candidates;
            best = pool[0];
            certain = pool.Count == 1;
            string what = marks is CornerCrossMarks ? "corner crosses" : "row/column labels";
            notes.Add(certain ? $"No {what} found; assumed the card is the right way up."
                              : $"No {what} found and some edge squares are too faint to see, so the grid may be off by a row or column: check the boxes.");
        }
        else if (!certain)
            notes.Add("The labels do not settle the placement clearly: check the boxes sit on the right squares.");
        var rivals = labelled && !certain ? ranked.Where(p => p.Score >= 1.0).Take(8).ToList() : [];
        if (best.TurnDegrees != 0)
            notes.Add($"The card is {DescribeTurn(best.TurnDegrees)} from the standard orientation (labels readable).");
        return best with { Certain = certain, Notes = notes, Rivals = rivals };
    }

    /// <summary>Plain words for a clockwise turn of the card in the image.</summary>
    public static string DescribeTurn(int degrees) => (((degrees % 360) + 360) % 360) switch
    {
        0 => "the right way up",
        90 => "turned 90° clockwise",
        180 => "upside down",
        270 => "turned 90° anticlockwise",
        var d => $"turned {d}°",
    };

    private static double LabelFraction(Gradient grad, Homography toWork, double baseInk, IEnumerable<(double c, double r)> spots,
                                        double halfPitches = 0.3)
    {
        int seen = 0, inked = 0;
        foreach (var (c, r) in spots)
        {
            var p = toWork.Map(c, r);
            double half = halfPitches * LocalPitchWork(toWork, c, r);
            var (ink, coh) = grad.Window(p, half);
            if (double.IsNaN(ink)) continue;
            seen++;
            if (ink > LabelInkRatio * baseInk && coh < StraightEdgeCoherence) inked++;
        }
        return seen == 0 ? 0 : inked / (double)seen;
    }

    private static double LocalPitch(Homography h, double i, double j) =>
        ((h.Map(i + 0.5, j) - h.Map(i - 0.5, j)).Length + (h.Map(i, j + 0.5) - h.Map(i, j - 0.5)).Length) / 2;

    private static double LocalPitchWork(Homography h, double c, double r) => LocalPitch(h, c, r);

    // ------------------------------------------------------------------ helpers

    private static double Median(IEnumerable<double> values)
    {
        var a = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        return a.Length == 0 ? double.NaN : a[a.Length / 2];
    }

    /// <summary>Buckets points so "everything within r of p" does not scan the whole list.</summary>
    private sealed class SpatialHash
    {
        private readonly List<PointD> _pts;
        private readonly double _cell;
        private readonly Dictionary<(int, int), List<int>> _buckets = [];

        public SpatialHash(List<PointD> pts, double cell)
        {
            _pts = pts;
            _cell = Math.Max(cell, 1);
            for (int i = 0; i < pts.Count; i++)
            {
                var key = ((int)Math.Floor(pts[i].X / _cell), (int)Math.Floor(pts[i].Y / _cell));
                if (!_buckets.TryGetValue(key, out var list)) _buckets[key] = list = [];
                list.Add(i);
            }
        }

        public IEnumerable<int> Near(PointD p, double r)
        {
            int span = (int)Math.Ceiling(r / _cell);
            int bx = (int)Math.Floor(p.X / _cell), by = (int)Math.Floor(p.Y / _cell);
            for (int dy = -span; dy <= span; dy++)
                for (int dx = -span; dx <= span; dx++)
                    if (_buckets.TryGetValue((bx + dx, by + dy), out var list))
                        foreach (int i in list)
                            if ((_pts[i] - p).Length <= r) yield return i;
        }
    }
}

/// <summary>
/// What a card carries beside its grid that shows which way up it is and where the grid
/// ends - needed because the faintest squares may not show at all.
/// </summary>
public abstract record CardMarks
{
    /// <summary>MakeIt's colour test and test array: a value label left of every row and under every column.</summary>
    public static CardMarks MakeItLabels { get; } = new MakeItLabelMarks();

    /// <summary>An Atlas card engraved with <see cref="Grids.CornerMarks"/>: crosses at three corners.</summary>
    public static CardMarks AtlasCorners(Grids.GridLayout layout) =>
        new CornerCrossMarks(Grids.CornerMarks.OffsetPitches(layout), Grids.CornerMarks.HalfSizePitches(layout)) { Layout = layout };
}

public sealed record MakeItLabelMarks : CardMarks;

/// <param name="OffsetPitches">Distance from a corner cell's centre to its cross, in cell pitches.</param>
/// <param name="HalfSizePitches">Half a cross's size, in cell pitches.</param>
public sealed record CornerCrossMarks(double OffsetPitches, double HalfSizePitches) : CardMarks
{
    /// <summary>The grid's layout, so its number dots can settle an open placement (<see cref="Grids.CardId"/>).</summary>
    public Grids.GridLayout? Layout { get; init; }
}
