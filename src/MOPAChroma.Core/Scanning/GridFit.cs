namespace MOPAChroma.Scanning;

public sealed record GridFitResult(GridGeometry Grid, double RmsPixels, int CellsUsed);

/// <summary>
/// Refines a roughly placed grid (three clicks) against the squares actually in the image.
///
/// For every cell whose window has clear contrast, the square is found as the centroid of
/// the pixels brighter than halfway between the window's dark and bright ends. An affine
/// grid is then least-squares fitted through those centroids and the whole process
/// repeated, so a few misjudged clicks or a tilted card do not matter. Dark cells that
/// cannot be found reliably simply do not vote; the fit places them from the rest.
///
/// A grid that carries a perspective <see cref="GridGeometry.Warp"/> (a photo taken at an
/// angle) is refitted as a full homography instead of an affine grid, and each cell's
/// window is sized from its own local pitch, since squares far from the camera are smaller.
/// </summary>
public static class GridFit
{
    /// <summary>Minimum spread (R+G+B, 0..765) inside a window for a cell to vote.</summary>
    public const int MinContrast = 45;

    /// <summary>
    /// Iterates until the grid moves less than 0.05 px anywhere, or <paramref name="iterations"/>
    /// passes. Each pass only partly corrects a misplaced window (a window that clips one side
    /// of its square pulls the centroid toward itself), so several passes are normal.
    /// <paramref name="strict"/> lets a cell vote only when what it holds looks like a square,
    /// of either polarity: for cards where many cells are blank, lighter than the metal or
    /// over a shadow (titanium), which otherwise drag the fit off the grid.
    /// </summary>
    public static GridFitResult Refine(RgbImage img, GridGeometry start, int iterations = 12, bool squaresDarker = false, bool strict = false)
    {
        bool projective = start.Warp is not null;
        var grid = start;
        double rms = double.NaN;
        int used = 0;

        for (int it = 0; it < iterations; it++)
        {
            var rows = new List<(double c, double r)>();
            var pts = new List<PointD>();
            for (int r = 0; r < grid.Rows; r++)
                for (int c = 0; c < grid.Columns; c++)
                    if (Centroid(img, grid.Centre(r, c), (int)(grid.PitchAt(r, c) * 0.5), squaresDarker, strict) is { } p)
                    {
                        rows.Add((c, r));
                        pts.Add(p);
                    }

            if (pts.Count < 3) break; // nothing to fit to: keep the clicked grid
            var fitted = Fit(rows, pts, grid.Rows, grid.Columns, projective);
            if (fitted is null) break;

            // Dim or blotchy squares give poor centroids. Drop anything that disagrees with
            // the grid by more than 3x the median disagreement (and over 1.5 px), then refit.
            var res = Residuals(fitted, rows, pts);
            var sorted = res.OrderBy(x => x).ToArray();
            double limit = Math.Max(1.5, 3 * sorted[sorted.Length / 2]);
            var keepRows = new List<(double c, double r)>();
            var keepPts = new List<PointD>();
            for (int i = 0; i < res.Length; i++)
                if (res[i] <= limit) { keepRows.Add(rows[i]); keepPts.Add(pts[i]); }
            if (keepPts.Count >= 3 && keepPts.Count < pts.Count && Fit(keepRows, keepPts, grid.Rows, grid.Columns, projective) is { } refit)
            {
                fitted = refit;
                rows = keepRows;
                pts = keepPts;
                res = Residuals(fitted, rows, pts);
            }

            double moved = Math.Max(
                Math.Max((fitted.Centre(0, 0) - grid.Centre(0, 0)).Length,
                         (fitted.Centre(0, grid.Columns - 1) - grid.Centre(0, grid.Columns - 1)).Length),
                Math.Max((fitted.Centre(grid.Rows - 1, 0) - grid.Centre(grid.Rows - 1, 0)).Length,
                         (fitted.Centre(grid.Rows - 1, grid.Columns - 1) - grid.Centre(grid.Rows - 1, grid.Columns - 1)).Length));
            grid = fitted;
            used = pts.Count;
            rms = Math.Sqrt(res.Sum(x => x * x) / res.Length);
            if (moved < 0.05) break;
        }
        return new GridFitResult(grid, rms, used);
    }

    private static double[] Residuals(GridGeometry g, List<(double c, double r)> idx, List<PointD> pts)
    {
        var res = new double[pts.Count];
        for (int i = 0; i < pts.Count; i++)
            res[i] = (g.Centre((int)idx[i].r, (int)idx[i].c) - pts[i]).Length;
        return res;
    }

    private static GridGeometry? Fit(List<(double c, double r)> idx, List<PointD> pts, int rows, int cols, bool projective)
    {
        // A homography needs well-spread points; with only a handful, fall back to affine.
        if (projective && pts.Count >= 8 &&
            Homography.Fit(idx.Select(i => new PointD(i.c, i.r)).ToList(), pts) is { } h)
            return GridGeometry.FromWarp(h, rows, cols);
        var affine = FitAffine(idx, pts, rows, cols);
        return affine is not null && projective ? GridGeometry.FromWarp(affine.AsWarp(), rows, cols) : affine;
    }

    /// <summary>
    /// Centroid of the square inside a window. Squares are usually brighter than the bare
    /// surface (oxide on polished steel); on a light material they can be darker, and
    /// <paramref name="darker"/> flips the test. On titanium a card holds both (unmarked-
    /// looking cells come out lighter than the bare metal, coloured ones darker), so when
    /// the expected side does not look like a square the other side is tried. A window that
    /// only holds a shadow or a smudge edge selects a half of the window, not a square, and
    /// does not vote. Those checks apply in <paramref name="strict"/> mode, used when the
    /// ordinary fit wanders (<see cref="Refine"/>).
    /// </summary>
    internal static PointD? Centroid(RgbImage img, PointD centre, int half, bool darker = false, bool strict = false) =>
        strict ? SquareCentroid(img, centre, half, darker, true) ?? SquareCentroid(img, centre, half, !darker, true)
               : SquareCentroid(img, centre, half, darker, false);

    private static PointD? SquareCentroid(RgbImage img, PointD centre, int half, bool darker, bool strict)
    {
        int x0 = (int)centre.X - half, y0 = (int)centre.Y - half, n = 2 * half;
        if (half < 3 || x0 < 0 || y0 < 0 || x0 + n > img.Width || y0 + n > img.Height) return null;

        // 5th and 95th percentiles by histogram, cheap and robust to specks.
        var hist = new int[766];
        for (int y = y0; y < y0 + n; y++)
            for (int x = x0; x < x0 + n; x++)
                hist[img.Luma3(x, y)]++;
        int total = n * n, lo = Percentile(hist, total * 0.05), hi = Percentile(hist, total * 0.95);
        if (hi - lo < MinContrast) return null;
        int thr = (lo + hi) / 2;

        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        int count = 0, edge = 0, edgeAll = 0, band = Math.Max(1, n / 16);
        for (int y = y0; y < y0 + n; y++)
            for (int x = x0; x < x0 + n; x++)
            {
                bool onEdge = x < x0 + band || x >= x0 + n - band || y < y0 + band || y >= y0 + n - band;
                if (onEdge) edgeAll++;
                if ((img.Luma3(x, y) > thr) != darker)
                {
                    sx += x; sy += y; sxx += (double)x * x; syy += (double)y * y; sxy += (double)x * y; count++;
                    if (onEdge) edge++;
                }
            }

        // A square fills roughly (0.8 pitch)² of a pitch-sized window: reject anything
        // far from that - a label, a speck, or a window straddling two squares.
        double frac = count / (double)total;
        if (frac < 0.3 || frac > 0.9) return null;
        double mx = sx / count, my = sy / count;
        // The square lies inside the window, so little of what is selected runs along the
        // window's edge: a shadow or smudge edge (half the window) and the ring round a square
        // of the other polarity both cover much of it. A filled square or a slightly stretched
        // one (a scanner's x and y scales can differ) has second moments near a²/12 each way.
        double vxx = sxx / count - mx * mx, vyy = syy / count - my * my, vxy = sxy / count - mx * my;
        double tr = vxx + vyy, disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - (vxx * vyy - vxy * vxy)));
        double l1 = tr / 2 + disc, l2 = tr / 2 - disc, ideal = count / 12.0;
        if (strict && (edge > 0.25 * edgeAll || l2 <= 0 || l1 / l2 > 2.5 || tr < 0.6 * 2 * ideal || tr > 1.8 * 2 * ideal)) return null;
        return new PointD(mx + 0.5, my + 0.5);
    }

    private static int Percentile(int[] hist, double rank)
    {
        double acc = 0;
        for (int v = 0; v < hist.Length; v++)
        {
            acc += hist[v];
            if (acc >= rank) return v;
        }
        return hist.Length - 1;
    }

    /// <summary>Least squares for p = O + c·U + r·V, solved separately for x and y.</summary>
    private static GridGeometry? FitAffine(List<(double c, double r)> idx, List<PointD> pts, int rows, int cols)
    {
        double scc = 0, scr = 0, sc = 0, srr = 0, sr = 0, s1 = idx.Count;
        double xc = 0, xr = 0, x1 = 0, yc = 0, yr = 0, y1 = 0;
        for (int i = 0; i < idx.Count; i++)
        {
            var (c, r) = idx[i];
            scc += c * c; scr += c * r; sc += c; srr += r * r; sr += r;
            xc += c * pts[i].X; xr += r * pts[i].X; x1 += pts[i].X;
            yc += c * pts[i].Y; yr += r * pts[i].Y; y1 += pts[i].Y;
        }
        var m = new double[,] { { scc, scr, sc }, { scr, srr, sr }, { sc, sr, s1 } };
        var bx = Solve3(m, xc, xr, x1);
        var by = Solve3(m, yc, yr, y1);
        if (bx is null || by is null) return null;
        return new GridGeometry(new PointD(bx[2], by[2]), new PointD(bx[0], by[0]), new PointD(bx[1], by[1]), rows, cols);
    }

    private static double[]? Solve3(double[,] a, double b0, double b1, double b2)
    {
        double det = Det(a[0, 0], a[0, 1], a[0, 2], a[1, 0], a[1, 1], a[1, 2], a[2, 0], a[2, 1], a[2, 2]);
        if (Math.Abs(det) < 1e-9) return null; // e.g. all voting cells in one row
        double d0 = Det(b0, a[0, 1], a[0, 2], b1, a[1, 1], a[1, 2], b2, a[2, 1], a[2, 2]);
        double d1 = Det(a[0, 0], b0, a[0, 2], a[1, 0], b1, a[1, 2], a[2, 0], b2, a[2, 2]);
        double d2 = Det(a[0, 0], a[0, 1], b0, a[1, 0], a[1, 1], b1, a[2, 0], a[2, 1], b2);
        return [d0 / det, d1 / det, d2 / det];
    }

    private static double Det(double a, double b, double c, double d, double e, double f, double g, double h, double i) =>
        a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
}
