using LumosUltra.Jobs;

namespace MOPAChroma.Grids;

/// <summary>
/// Generates a WeCreat Lumos Ultra gcode.gc job directly from a planned grid, bypassing
/// MakeIt's own "Color Test" wizard entirely. That wizard only supports sweeping two
/// parameters at a time, linearly interpolated across rows/columns, so it cannot
/// represent a grid whose cells vary power, speed, line density, frequency and pulse
/// width independently per cell - exactly what <c>TestGrid.Arrange</c> produces.
///
/// The job file itself is written by <see cref="MakeItJob"/> (LumosUltra.Jobs), which
/// carries the confirmed preamble, postamble and per-stroke encoding; see its notes and
/// docs/CALIBRATION-FINDINGS.md. This class only decides what to draw where.
/// </summary>
public static class GcodeExport
{
    /// <summary>Fraction of a cell's width reserved (split evenly) for laser-off fly-in
    /// and fly-out either side of the burn stroke on every scan line, mirroring the
    /// confirmed real pattern (a zero-power move before and after the powered stroke) at
    /// a scale suited to small cells rather than the ~1mm margin seen on real 5mm cells.</summary>
    private const double MarginFraction = 0.1;

    /// <param name="grid">The planned grid to engrave.</param>
    /// <param name="centerX">Machine X of the canvas centre (bed home), mm. Default
    /// matches every real capture seen so far (canvas border 0 0 210 210).</param>
    /// <param name="centerY">Machine Y of the canvas centre, mm.</param>
    /// <param name="cornerMarks">Also engrave the three corner crosses (see <see cref="CornerMarks"/>)
    /// that let a photo or scan of the card be read automatically.</param>
    public static string ToGcode(TestGrid grid, double centerX = 105, double centerY = 105, bool cornerMarks = false)
    {
        var l = grid.Layout;
        double originX = centerX - l.WidthMm / 2;
        double originY = centerY - l.HeightMm / 2;

        var job = new MakeItJob();

        foreach (var cell in grid.Cells)
            AppendCell(job, cell, l, originX, originY);

        if (cornerMarks && grid.Cells.Count > 0)
            AppendCornerMarks(job, grid, originX, originY);

        return job.Finish();
    }

    /// <summary>
    /// Three small crosses beside the grid - above the first and last columns' top cells,
    /// and below the first column's bottom cell; never at the bottom-right. An Atlas card
    /// has no MakeIt value labels, and its faintest cells (no mark) may not show at all, so
    /// without these an image of the card cannot tell which way up it is or where the grid
    /// ends. They sit in the space above and below the grid that the coupon fit already
    /// reserves, so the grid itself does not move.
    /// <para>Drawn as vector strokes with the settings of the grid's most energetic cell
    /// (highest power per unit speed): the cell most likely to leave a visible line.</para>
    /// </summary>
    private static void AppendCornerMarks(MakeItJob job, TestGrid grid, double originX, double originY)
    {
        var l = grid.Layout;
        // The marks copy the grid's hottest cell (most heat per area, passes included) exactly:
        // they are filled the way a cell is - same line spacing, speed, power, frequency, pulse
        // width and passes - so they take that cell's heat per area and show at least as well as
        // it does. (Single thin strokes at a cell's power and speed put far less heat into the
        // surface than the cell itself and can leave no mark at all: titanium, 2026-10-01.)
        var s = grid.Cells.MaxBy(c => HeatKey(c.Settings))!.Settings;
        double lp = s.LinePitchMm;
        if (!(lp > 0)) lp = 0.05;
        int passes = Math.Max(1, s.Passes);
        double size = CornerMarks.SizeMm(l), half = size / 2;
        double pitch = l.CellSizeMm + l.GapMm;
        double offset = CornerMarks.OffsetMm(l);

        // Cell (c, r) centre in machine mm; the marks sit `offset` above or below a corner cell.
        (double x, double y) Centre(int c, int r) => (originX + c * pitch + l.CellSizeMm / 2, originY + r * pitch + l.CellSizeMm / 2);
        var (tlx, tly) = Centre(0, 0);
        var (trx, _) = Centre(l.Columns - 1, 0);
        var (blx, bly) = Centre(0, l.Rows - 1);

        job.Pulse(s.FrequencyKHz, s.PulseWidthNs).Feed(s.SpeedMmPerS);
        int power = MakeItJob.PowerS(s.PowerPercent);

        // The grid number as a row of dots between the two top crosses (CardId).
        foreach (var (x, y, d) in CardId.DotsMm(l, grid.Number, originX, originY))
            job.Fill(x - d / 2, y - d / 2, x + d / 2, y + d / 2, lp, power, passes);

        // Corner crosses: two filled bars each.
        double bar = Math.Min(MarkBarMm, half);
        foreach (var (cx, cy) in new[] { (tlx, tly - offset), (trx, tly - offset), (blx, bly + offset) })
        {
            job.Fill(cx - half, cy - bar / 2, cx + half, cy + bar / 2, lp, power, passes);
            job.Fill(cx - bar / 2, cy - half, cx + bar / 2, cy + half, lp, power, passes);
        }

        // "Grid 29" for people, in the bottom margin (GridLabel): each stroke filled to
        // GridLabel.StrokeMm wide with parallel lines at the cell's spacing.
        foreach (var line in GridLabel.StrokesMm(l, grid.Number, originX, originY))
            for (int pass = 0; pass < passes; pass++)
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    var (ax, ay) = line[i];
                    var (bx, by) = line[i + 1];
                    job.Band(ax, ay, bx, by, GridLabel.StrokeMm, lp, power);
                }
    }

    /// <summary>Width of a corner cross's arms, mm.</summary>
    public const double MarkBarMm = 0.25;

    /// <summary>Heat per area up to the source's power, for picking the hottest cell (no source needed).</summary>
    private static double HeatKey(MopaSettings s) =>
        s.SpeedMmPerS > 0 && s.LinePitchMm > 0 ? Math.Max(1, s.Passes) * s.PowerPercent / (s.SpeedMmPerS * s.LinePitchMm) : 0;

    /// <summary>
    /// The machine-millimetre box a job covers (its cells, and the corner crosses if any):
    /// what the camera preview outlines, and what must stay on the bed.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) Extents(TestGrid grid, double centerX = 105, double centerY = 105, bool cornerMarks = false)
    {
        var l = grid.Layout;
        double x0 = centerX - l.WidthMm / 2, y0 = centerY - l.HeightMm / 2;
        double minX = x0, minY = y0, maxX = x0 + l.WidthMm, maxY = y0 + l.HeightMm;
        if (cornerMarks)
        {
            double half = CornerMarks.SizeMm(l) / 2, offset = CornerMarks.OffsetMm(l);
            double pitch = l.CellSizeMm + l.GapMm;
            minY = Math.Min(minY, y0 + l.CellSizeMm / 2 - offset - half);
            maxY = Math.Max(maxY, y0 + (l.Rows - 1) * pitch + l.CellSizeMm / 2 + offset + half);
            minX = Math.Min(minX, x0 + l.CellSizeMm / 2 - half);
            maxX = Math.Max(maxX, x0 + (l.Columns - 1) * pitch + l.CellSizeMm / 2 + half);
        }
        return (minX, minY, maxX, maxY);
    }

    /// <summary>The camera calibration target; now <see cref="LumosUltra.Camera.CalibrationTarget"/>, kept here for callers.</summary>
    public static string CalibrationTarget(MopaSettings s) => LumosUltra.Camera.CalibrationTarget.Gcode(s);

    private static void AppendCell(MakeItJob job, GridCell cell, GridLayout l, double originX, double originY)
    {
        var s = cell.Settings;
        double pitch = s.LinePitchMm;
        if (double.IsNaN(pitch) || pitch <= 0) return; // no line density given - nothing to draw

        double x0 = originX + cell.Column * (l.CellSizeMm + l.GapMm);
        double y0 = originY + cell.Row * (l.CellSizeMm + l.GapMm);

        job.Pulse(s.FrequencyKHz, s.PulseWidthNs).Feed(s.SpeedMmPerS);
        job.RasterCell(x0, y0, l.CellSizeMm, pitch, MakeItJob.PowerS(s.PowerPercent), s.Passes, MarginFraction);
    }
}

/// <summary>
/// Where Atlas puts its corner crosses, shared by <see cref="GcodeExport"/> (which engraves
/// them) and the card reader (which looks for them), so the two always agree.
/// </summary>
public static class CornerMarks
{
    /// <summary>Length of each arm of a cross, mm: a third of a cell, between 1.5 and 3 mm.</summary>
    public static double SizeMm(GridLayout l) => Math.Clamp(l.CellSizeMm * 0.35, 1.5, 3.0);

    /// <summary>Distance from a corner cell's centre to its cross, mm: clear of the cell by the gap.</summary>
    public static double OffsetMm(GridLayout l) => l.CellSizeMm / 2 + l.GapMm + SizeMm(l) / 2;

    /// <summary>The same distance in cell pitches (cell + gap), the unit the reader works in.</summary>
    public static double OffsetPitches(GridLayout l) => OffsetMm(l) / (l.CellSizeMm + l.GapMm);

    /// <summary>Half the cross size in cell pitches: the window the reader inspects around each mark.</summary>
    public static double HalfSizePitches(GridLayout l) => SizeMm(l) / 2 / (l.CellSizeMm + l.GapMm);
}
