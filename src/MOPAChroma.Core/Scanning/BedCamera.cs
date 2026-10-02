namespace MOPAChroma.Scanning;

/// <summary>
/// Where a point on the laser bed (machine millimetres, the G-code's own X/Y) appears in a
/// picture from the laser's built-in camera, so a job can be drawn over the picture before
/// anything is engraved, and moved onto the blank by dragging.
/// <para>
/// A plane-to-plane perspective map (<see cref="Homography"/>) does most of the work; the
/// camera's wide lens also bends straight lines, so with enough points a radial lens
/// correction about the picture centre is fitted as well. Fitted from crosses engraved at known
/// bed positions (<see cref="BedCameraTarget"/>) and clicked in a picture of them.
/// </para>
/// <para>
/// Only true at the height it was calibrated at: a thicker blank sits closer to the camera
/// and shifts a little, more towards the edges of the bed.
/// </para>
/// </summary>
public sealed record BedCamera(
    double[] H, double K1, double K2, double RmsMm, double MaxMm, int Points,
    int ImageWidth, int ImageHeight, DateTimeOffset Calibrated)
{
    // Lens model: a picture point d is "straightened" to u = c + (d - c)(1 + K1 r² + K2 r⁴),
    // r = |d - c| / R, about the picture centre c, R = half the longer side. The bed then
    // maps to straightened points by the homography H.
    private PointD Centre => new(ImageWidth / 2.0, ImageHeight / 2.0);
    private double R => Math.Max(ImageWidth, ImageHeight) / 2.0;

    private static PointD Straighten(PointD d, PointD c, double r, double k1, double k2)
    {
        double nx = (d.X - c.X) / r, ny = (d.Y - c.Y) / r, r2 = nx * nx + ny * ny;
        double f = 1 + k1 * r2 + k2 * r2 * r2;
        return new PointD(c.X + (d.X - c.X) * f, c.Y + (d.Y - c.Y) * f);
    }

    /// <summary>Bed millimetres to image pixels.</summary>
    public PointD ToImage(PointD bed)
    {
        var u = new Homography(H).Map(bed);
        // Invert the straightening by fixed-point steps: d = c + (u - c) / f(d).
        var c = Centre;
        var d = u;
        for (int i = 0; i < 20; i++)
        {
            double nx = (d.X - c.X) / R, ny = (d.Y - c.Y) / R, r2 = nx * nx + ny * ny;
            double f = 1 + K1 * r2 + K2 * r2 * r2;
            d = new PointD(c.X + (u.X - c.X) / f, c.Y + (u.Y - c.Y) / f);
        }
        return d;
    }

    /// <summary>
    /// Where the squares of a grid engraved with its centre at (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>) on the bed appear in the picture (as GcodeExport lays them out).
    /// </summary>
    public GridGeometry? GridAt(MOPAChroma.Grids.GridLayout l, double centerX, double centerY)
    {
        if (l.Rows < 2 || l.Columns < 2) return null;
        double pitch = l.CellSizeMm + l.GapMm;
        double x0 = centerX - l.WidthMm / 2 + l.CellSizeMm / 2, y0 = centerY - l.HeightMm / 2 + l.CellSizeMm / 2;
        var src = new List<PointD>();
        var dst = new List<PointD>();
        for (int r = 0; r < l.Rows; r++)
            for (int c = 0; c < l.Columns; c++)
            {
                src.Add(new PointD(c, r));
                dst.Add(ToImage(new PointD(x0 + c * pitch, y0 + r * pitch)));
            }
        return Homography.Fit(src, dst) is { } h ? GridGeometry.FromWarp(h, l.Rows, l.Columns) : null;
    }

    /// <summary>Image pixels to bed millimetres.</summary>
    public PointD ToBed(PointD image) =>
        (new Homography(H).Inverse() ?? Homography.Identity).Map(Straighten(image, Centre, R, K1, K2));

    /// <summary>
    /// Fits the map from matching points (bed mm ↔ image px): a homography from four or more
    /// points, and with six or more the lens bend too (the straightening that lets one
    /// homography fit best). <see cref="K2"/> is kept for a later, richer fit and is 0 here. Errors are reported in bed millimetres. Null when the
    /// points cannot determine a map.
    /// </summary>
    public static BedCamera? Fit(IReadOnlyList<PointD> bed, IReadOnlyList<PointD> image, int imageWidth, int imageHeight, DateTimeOffset when)
    {
        if (bed.Count != image.Count || bed.Count < 4) return null;
        var c = new PointD(imageWidth / 2.0, imageHeight / 2.0);
        double r = Math.Max(imageWidth, imageHeight) / 2.0;

        (double err, Homography? h) Try(double k1, double k2)
        {
            var u = image.Select(d => Straighten(d, c, r, k1, k2)).ToArray();
            var h = Homography.Fit(bed, u);
            var inv = h?.Inverse();
            if (h is null || inv is null) return (double.MaxValue, null);
            return (bed.Select((b, i) => { var e = inv.Map(u[i]) - b; return e.X * e.X + e.Y * e.Y; }).Sum(), h);
        }

        double bk1 = 0;
        if (bed.Count >= 6)
        {
            // Coarse steps, then shrink around the best.
            double step = 0.1;
            for (int round = 0; round < 8; round++, step /= 3)
            {
                double best = double.MaxValue, nk1 = bk1;
                // K2 stays 0: nine points pin down one lens term well, and a second one
                // fitted from them bends wildly beyond the outer crosses.
                for (int i = -4; i <= 4; i++)
                {
                    double k1 = bk1 + i * step;
                    var (e, _) = Try(k1, 0);
                    if (e < best) { best = e; nk1 = k1; }
                }
                bk1 = nk1;
            }
        }
        var (_, hb) = Try(bk1, 0);
        if (hb is null) return null;
        var cam = new BedCamera(hb.Coefficients.ToArray(), bk1, 0, 0, 0, bed.Count, imageWidth, imageHeight, when);
        var errs = bed.Select((b, i) => (cam.ToBed(image[i]) - b).Length).ToArray();
        return cam with { RmsMm = Math.Sqrt(errs.Average(e => e * e)), MaxMm = errs.Max() };
    }
}

/// <summary>
/// The calibration target: nine crosses spread unevenly over the bed, each with 1 to 9 short
/// bars beside it. The uneven layout matters: on a regular 3 × 3 layout a mirrored or turned
/// matching of crosses to positions fits just as well as the right one, and a calibration
/// made that way puts every job on the wrong side of the bed. Here only the right matching
/// fits, so the crosses can be clicked in any order and <see cref="Match"/> works out which
/// is which.
/// </summary>
public static class BedCameraTarget
{
    /// <summary>Changes whenever the layout does; calibrations from another layout are not trusted.</summary>
    public const int Version = 2;

    public static readonly PointD[] Points =
    [
        new(28, 34), new(178, 26), new(186, 170), new(38, 184),
        new(98, 112), new(122, 42), new(168, 98), new(92, 176), new(24, 118),
    ];

    /// <summary>Half the length of each arm of a cross, mm.</summary>
    public const double ArmMm = 4;
    /// <summary>Bars beside each cross: their spacing, length, and gap from the cross centre, mm.</summary>
    public const double BarSpacingMm = 1.2, BarLengthMm = 3, BarGapMm = 6;

    /// <summary>The outcome of matching clicked crosses to target positions.</summary>
    /// <param name="Index">For each click, the target point it is (or -1 for a click that matches none).</param>
    /// <param name="Camera">The calibration from the matched clicks.</param>
    /// <param name="Margin">How much worse the next-best matching fits (mm); small means it could not tell.</param>
    public sealed record Matching(int[] Index, BedCamera Camera, double Margin);

    /// <summary>
    /// Works out which target cross each click is, whatever order they were clicked in and
    /// however the picture is turned or mirrored: tries every way of matching four widely
    /// spread clicks to four target points, keeps the one under which the other clicks land on
    /// target points, then fits the camera to all matched clicks. Null with fewer than five
    /// clicks or when nothing fits.
    /// </summary>
    public static Matching? Match(IReadOnlyList<PointD> clicks, int imageWidth, int imageHeight, DateTimeOffset when)
    {
        int n = clicks.Count, m = Points.Length;
        if (n < 5) return null;

        // Four widely spread clicks pin the map down; trying the widest few sets means one
        // stray click (or a cross misread) cannot spoil every attempt.
        var quads = new List<(double area, int[] q)>();
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                for (int c = b + 1; c < n; c++)
                    for (int d = c + 1; d < n; d++)
                        quads.Add((HullArea([clicks[a], clicks[b], clicks[c], clicks[d]]), [a, b, c, d]));

        const double cap = 12; // mm: a click further than this from every target point counts as unmatched
        double best = double.MaxValue, second = double.MaxValue;
        int[]? bestIndex = null;
        foreach (var (_, quad) in quads.OrderByDescending(x => x.area).Take(12))
        {
            var q = quad.Select(i => clicks[i]).ToArray();
            for (int i = 0; i < m; i++)
                for (int j = 0; j < m; j++)
                {
                    if (j == i) continue;
                    for (int k = 0; k < m; k++)
                    {
                        if (k == i || k == j) continue;
                        for (int l = 0; l < m; l++)
                        {
                            if (l == i || l == j || l == k) continue;
                            var h = Homography.Fit([Points[i], Points[j], Points[k], Points[l]], q);
                            var inv = h?.Inverse();
                            if (inv is null) continue;
                            var index = new int[n];
                            var used = new bool[m];
                            double score = 0;
                            for (int c = 0; c < n; c++)
                            {
                                var bed = inv.Map(clicks[c]);
                                int near = -1;
                                double dmin = double.MaxValue;
                                for (int t = 0; t < m; t++)
                                {
                                    double dd = (Points[t] - bed).Length;
                                    if (dd < dmin) { dmin = dd; near = t; }
                                }
                                if (dmin > cap || used[near]) { index[c] = -1; score += cap * cap; continue; }
                                used[near] = true;
                                index[c] = near;
                                score += dmin * dmin;
                            }
                            if (SameMatching(index, bestIndex)) { best = Math.Min(best, score); continue; }
                            if (score < best) { second = best; best = score; bestIndex = index; }
                            else if (score < second) second = score;
                        }
                    }
                }
        }
        if (bestIndex is null) return null;
        var pairs = Enumerable.Range(0, n).Where(c => bestIndex[c] >= 0).ToArray();
        if (pairs.Length < 5) return null;
        var cam = BedCamera.Fit(pairs.Select(c => Points[bestIndex[c]]).ToArray(), pairs.Select(c => clicks[c]).ToArray(), imageWidth, imageHeight, when);
        if (cam is null) return null;
        return new Matching(bestIndex, cam, Math.Sqrt(Math.Max(0, second) / n) - Math.Sqrt(best / n));
    }

    private static bool SameMatching(int[] a, int[]? b) => b is not null && a.AsSpan().SequenceEqual(b);

    /// <summary>Area of the convex hull of four points.</summary>
    private static double HullArea(PointD[] p)
    {
        static double Shoelace(params PointD[] q)
        {
            double s = 0;
            for (int i = 0; i < q.Length; i++) { var a = q[i]; var b = q[(i + 1) % q.Length]; s += a.X * b.Y - b.X * a.Y; }
            return Math.Abs(s) / 2;
        }
        // A convex quadrilateral is the largest of the three ways round; a point inside the
        // other three leaves the largest triangle.
        return new[]
        {
            Shoelace(p[0], p[1], p[2], p[3]), Shoelace(p[0], p[1], p[3], p[2]), Shoelace(p[0], p[2], p[1], p[3]),
            Shoelace(p[0], p[1], p[2]), Shoelace(p[0], p[1], p[3]), Shoelace(p[0], p[2], p[3]), Shoelace(p[1], p[2], p[3]),
        }.Max();
    }
}
