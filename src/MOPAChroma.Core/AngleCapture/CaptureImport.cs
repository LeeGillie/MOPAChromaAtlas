using MOPAChroma.Atlas;
using MOPAChroma.Capture.Color;
using MOPAChroma.Capture.Frames;
using MOPAChroma.Capture.Geometry;
using MOPAChroma.Capture.Package;
using MOPAChroma.Color;
using MOPAChroma.Grids;
using MOPAChroma.Scanning;

namespace MOPAChroma.AngleCapture;

/// <summary>Where one cell is on the card, in the card's own millimetres (centre at the turntable's axis).</summary>
public sealed record CellPlace(string Label, double X, double Y, double SizeMm);

/// <summary>Every cell of a grid placed on the card.</summary>
public sealed record CellMap(IReadOnlyList<CellPlace> Cells)
{
    /// <summary>
    /// From the grid found (or placed by hand) on a package's flat plain frame: each cell's
    /// centre in that picture, taken back through the frame's fitted view onto the card.
    /// </summary>
    public static CellMap FromPlainFrame(GridGeometry grid, TestGrid g, CameraView plainView)
    {
        var cells = new List<CellPlace>();
        double fill = g.Layout.CellSizeMm / (g.Layout.CellSizeMm + g.Layout.GapMm);
        foreach (var c in g.Cells)
        {
            var px = grid.Centre(c.Row, c.Column);
            var right = grid.CentreAt(c.Row, c.Column + 0.5);
            if (plainView.Unproject(new Pixel(px.X, px.Y)) is not { } p || plainView.Unproject(new Pixel(right.X, right.Y)) is not { } q) continue;
            double pitch = 2 * Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
            cells.Add(new CellPlace(c.Label, p.X, p.Y, pitch * fill));
        }
        return new CellMap(cells);
    }

    /// <summary>
    /// A grid laid out centred on the card, row 0 at the top (+Y). Used for simulated runs,
    /// where the simulated card is drawn from this same map.
    /// </summary>
    public static CellMap Centred(TestGrid g)
    {
        double pitch = g.Layout.CellSizeMm + g.Layout.GapMm;
        int rows = g.Layout.Rows, cols = g.Layout.Columns;
        return new CellMap(g.Cells.Select(c => new CellPlace(c.Label,
            (c.Column - (cols - 1) / 2.0) * pitch, -(c.Row - (rows - 1) / 2.0) * pitch, g.Layout.CellSizeMm)).ToList());
    }
}

/// <summary>What an import did.</summary>
public sealed record CaptureImportReport(IReadOnlyList<AngleSet> Sets, int Points, int Overexposed, IReadOnlyList<string> Notes);

/// <summary>
/// Turns a capture package into per-cell colour-over-angle tables in the atlas.
/// <list type="number">
/// <item>Each cell is sampled in every lit frame through that frame's fitted view: a 7 x 7
/// lattice over the middle 60 % of the cell, in the card's millimetres, so perspective and
/// foreshortening are taken care of.</item>
/// <item>For each turn, tilt and LED, the brightest level that leaves the cell unclipped is
/// used, scaled by the level ratio measured on the reference tile (not the nominal one).</item>
/// <item>Colours are expressed relative to the 18 % grey tile under the same LED: a
/// per-light white balance and exposure in one.</item>
/// <item>The view, light and half-way angles are worked out for the cell's own position,
/// because the LEDs are close enough for them to differ across the card.</item>
/// </list>
/// Camera pictures are taken as sRGB-encoded; a camera with another response curve skews
/// brightness (not hue much) until the response is calibrated (TODO).
/// </summary>
public static class CaptureImport
{
    public const double TileReflectance = 0.18;
    public const int ClipLevel = 250;
    public const double MaxClippedShare = 0.02;

    public static CaptureImportReport Import(AtlasDocument doc, CapturePackage pkg, CellMap cells, bool allowSimulated,
                                             IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var session = pkg.Session;
        if (session.Simulated && !allowSimulated)
            throw new InvalidOperationException("This package was made by the simulated rig. Simulated data is only ever imported into the demo atlas.");
        var grid = doc.Grids.FirstOrDefault(g => g.Id == session.Grid.AtlasGridId)
                   ?? throw new InvalidOperationException("The package does not name a grid in this atlas (it may be a MakeIt test card, which is not supported yet).");
        var geo = session.Geometry;
        var notes = new List<string>();
        if (!session.Complete) notes.Add("The run did not finish; only what was captured is imported.");
        if (!session.Camera.Locked) notes.Add("The camera's settings were not all locked; colours lean on the reference tile.");

        var lit = pkg.Frames.Where(f => f.Kind == FrameKind.Lit && f.View is not null).ToList();
        var groups = lit.GroupBy(f => (f.TurnCommandedDeg, f.TiltCommandedDeg, f.Led ?? -1)).ToList();
        var points = cells.Cells.ToDictionary(c => c.Label, _ => new List<AnglePoint>());
        int overexposed = 0, done = 0;
        bool warnedTile = false;

        foreach (var grp in groups)
        {
            ct.ThrowIfCancellationRequested();
            var levels = grp.OrderByDescending(f => f.LevelPercent).ToList();
            var top = levels[0];
            // The reference tile, linear, at the brightest level, and each level's measured ratio to it.
            double[]? tileTop = top.Stats?.TileRgb is { } tr ? tr.Select(Lin).ToArray() : null;
            if (tileTop is null && !warnedTile) { notes.Add("The reference tile was not visible; nominal brightness ratios and no white balance were used."); warnedTile = true; }
            double Ratio(FrameRecord f)
            {
                double nominal = f.LevelPercent / top.LevelPercent;
                if (tileTop is null || f.Stats?.TileRgb is not { } t || t.Average() < 6) return nominal;
                double a = t.Select(Lin).Average(), b = tileTop.Average();
                return b > 1e-6 ? a / b : nominal;
            }
            double[] wb = tileTop is null ? [TileReflectance, TileReflectance, TileReflectance] : tileTop;

            var images = new Dictionary<FrameRecord, Frame>();
            Frame Img(FrameRecord f) => images.TryGetValue(f, out var i) ? i : images[f] = pkg.LoadFrame(f);

            foreach (var cell in cells.Cells)
            {
                Sampled? chosen = null; FrameRecord? from = null;
                foreach (var f in levels)
                {
                    var s = Sample(Img(f), f.View!, cell);
                    if (s is null) break;
                    if (s.Clipped <= MaxClippedShare) { chosen = s; from = f; break; }
                }
                if (chosen is null || from is null) { overexposed++; continue; }
                double k = Ratio(from);
                var rgb = new[] { chosen.R / k / wb[0] * TileReflectance, chosen.G / k / wb[1] * TileReflectance, chosen.B / k / wb[2] * TileReflectance };
                var view = from.View!;
                double tilt = from.TiltMeasuredDeg ?? from.TiltCommandedDeg, turn = from.TurnMeasuredDeg ?? from.TurnCommandedDeg;
                var angles = Angles(geo, view, cell, tilt, turn, from.Led ?? 0);
                double conf = Math.Clamp(1 - (from.PoseRmsPixels ?? 2) / 4 - chosen.Spread * 2, 0.05, 1);
                points[cell.Label].Add(new AnglePoint(turn, tilt, from.Led ?? -1, angles.View, angles.Light, angles.Half,
                    angles.LightAz, angles.ViewAz, rgb.Select(v => Math.Round(v, 5)).ToArray(),
                    ColorConvert.LinearToLab(rgb[0], rgb[1], rgb[2]), from.LevelPercent, Math.Round(conf, 3)));
            }
            progress?.Report((++done, groups.Count));
        }

        // Replace any earlier import of the same session.
        doc.AngleSets.RemoveAll(a => a.SessionId == session.Id);
        var now = DateTimeOffset.UtcNow;
        var sets = new List<AngleSet>();
        foreach (var c in grid.Cells)
        {
            if (!points.TryGetValue(c.Label, out var pl) || pl.Count == 0) continue;
            var set = new AngleSet
            {
                SessionId = session.Id, Package = pkg.Folder,
                MaterialId = grid.MaterialId, SourceId = grid.SourceId, LensId = grid.LensId,
                GridId = grid.Id, CellLabel = c.Label, Settings = c.Settings,
                Simulated = session.Simulated, CapturedUtc = session.StartedUtc, ImportedUtc = now,
                Quality = pkg.Checks?.Overall.ToString().ToLowerInvariant() ?? "",
                Points = pl,
                Notes = session.Simulated ? "SIMULATED (capture demo)" : "",
            };
            sets.Add(set);
            doc.AngleSets.Add(set);
        }
        if (overexposed > 0) notes.Add($"{overexposed} readings were overexposed at every brightness and left out.");
        return new CaptureImportReport(sets, sets.Sum(s => s.Points.Count), overexposed, notes);
    }

    private static double Lin(double v8) => CaptureColor.ToLinear(Math.Clamp(v8, 0, 255) / 255);

    private sealed record Sampled(double R, double G, double B, double Clipped, double Spread);

    /// <summary>Mean linear RGB over the middle of a cell, the share of clipped samples, and the relative spread of brightness.</summary>
    private static Sampled? Sample(Frame f, CameraView view, CellPlace cell)
    {
        const int n = 7;
        double half = cell.SizeMm * 0.3;
        double r = 0, g = 0, b = 0, sumY = 0, sumY2 = 0; int count = 0, clipped = 0;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                var px = view.Project(new Vec3(cell.X - half + 2 * half * i / (n - 1), cell.Y - half + 2 * half * j / (n - 1), 0));
                int x = (int)Math.Floor(px.U), y = (int)Math.Floor(px.V);
                if (x < 0 || y < 0 || x >= f.Width || y >= f.Height) continue;
                int k = (y * f.Width + x) * 3;
                byte pr = f.Pixels[k], pg = f.Pixels[k + 1], pb = f.Pixels[k + 2];
                if (pr >= ClipLevel || pg >= ClipLevel || pb >= ClipLevel) clipped++;
                double lr = Lin(pr), lg = Lin(pg), lb = Lin(pb);
                r += lr; g += lg; b += lb;
                double yv = 0.2126 * lr + 0.7152 * lg + 0.0722 * lb;
                sumY += yv; sumY2 += yv * yv; count++;
            }
        if (count < n * n / 2) return null;
        double mean = sumY / count, sd = Math.Sqrt(Math.Max(0, sumY2 / count - mean * mean));
        return new Sampled(r / count, g / count, b / count, (double)clipped / count, mean > 1e-6 ? sd / mean : 0);
    }

    internal readonly record struct CellAngles(double View, double Light, double Half, double LightAz, double ViewAz);

    /// <summary>Angles at the cell's own position (the LEDs are close: they differ across the card).</summary>
    internal static CellAngles Angles(RigGeometry geo, CameraView view, CellPlace cell, double tilt, double turn, int led)
    {
        var p = RigGeometry.ToWorld(new Vec3(cell.X, cell.Y, 0), tilt, turn);
        var nrm = RigGeometry.Normal(tilt);
        var toCam = (view.CameraPosition - p).Normalized();
        var toLed = (geo.Led(Math.Clamp(led, 0, geo.LedAnglesDeg.Count - 1)) - p).Normalized();
        var half = (toCam + toLed).Normalized();
        static double Deg(double c) => Math.Acos(Math.Clamp(c, -1, 1)) * 180 / Math.PI;
        double Az(Vec3 dir)
        {
            var a = RigGeometry.ToCard(p, tilt, turn);
            var b = RigGeometry.ToCard(p + dir, tilt, turn);
            return Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
        }
        return new CellAngles(Deg(nrm.Dot(toCam)), Deg(nrm.Dot(toLed)), Deg(nrm.Dot(half)), Az(toLed), Az(toCam));
    }
}
