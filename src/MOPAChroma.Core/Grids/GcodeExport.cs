using System.Globalization;
using System.Text;
using MOPAChroma.Laser;

namespace MOPAChroma.Grids;

/// <summary>
/// Generates a WeCreat Lumos Ultra gcode.gc job directly from a planned grid, bypassing
/// MakeIt's own "Color Test" wizard entirely. That wizard only supports sweeping two
/// parameters at a time, linearly interpolated across rows/columns, so it cannot
/// represent a grid whose cells vary power, speed, line density, frequency and pulse
/// width independently per cell - exactly what <c>TestGrid.Arrange</c> produces.
///
/// The per-cell encoding is CONFIRMED against real hardware: a known 3x3 MakeIt-wizard
/// grid was captured via FTP and cross-checked against the physical engraved part (the
/// part itself carries the axis labels and fixed settings). See
/// docs/CALIBRATION-FINDINGS.md for the full writeup. In short, per cell:
/// power -> <c>S = Power% * 10</c> on the raster burn stroke, frequency -> <c>M38F&lt;kHz&gt;</c>
/// directly, speed -> <c>G1F&lt;mm/s * 60&gt;</c>, line pitch -> <c>10mm / LineDensityPerCm</c>,
/// pulse width -> <c>M39P&lt;ns&gt;</c>.
///
/// The setup preamble and shutdown postamble are copied verbatim from a real
/// MakeIt-generated job capture rather than invented, since they are machine
/// initialisation/homing sequences not otherwise understood command-by-command - see
/// samples/gcode/minimal-test-segment.gc, which used this exact preamble/postamble and
/// was CONFIRMED to run correctly (including actually firing the laser) on real
/// hardware.
/// </summary>
public static class GcodeExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Machine-specific setup sequence, copied verbatim from a real MakeIt job
    /// capture. Not independently understood command-by-command; confirmed to work.</summary>
    private const string Preamble =
        "M15S1\n" +
        "M107X-105Y-105\n" +
        "M57A450B30\n" +
        "M19S0\n" +
        "M18S0\n" +
        "G90\n" +
        "G4P1\n" +
        "M46A1B0\n" +
        "G0X105Y105\n" +
        "M25S1\n" +
        "M26S1\n" +
        "M24S15\n" +
        "M15S70\n" +
        "M59A-1000B50\n" +
        "M1S1\n" +
        "M4S1000\n" +
        "M11S0.005\n";

    /// <summary>Return-home and laser-off shutdown sequence, copied verbatim from a real
    /// MakeIt job capture.</summary>
    private const string Postamble =
        "G0X105Y105\n" +
        "M9\n" +
        "G1 S0\n" +
        "M5\n" +
        "G90\n" +
        "M6\n";

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

        var sb = new StringBuilder();
        sb.Append(";wecreat 3.0.6\n");
        sb.Append(";canvas border: 0 0 210 210\n");
        sb.Append(Preamble);

        foreach (var cell in grid.Cells)
            AppendCell(sb, cell, l, originX, originY);

        if (cornerMarks && grid.Cells.Count > 0)
            AppendCornerMarks(sb, grid, originX, originY);

        sb.Append(Postamble);
        return sb.ToString();
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
    private static void AppendCornerMarks(StringBuilder sb, TestGrid grid, double originX, double originY)
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

        sb.Append($"M38F{(int)Math.Round(s.FrequencyKHz)}\n");
        sb.Append($"M39P{(int)Math.Round(s.PulseWidthNs)}\n");
        sb.Append("G0F15000\n");
        sb.Append($"G1F{(int)Math.Round(s.SpeedMmPerS * 60)}\n");
        int power = (int)Math.Round(Math.Clamp(s.PowerPercent, 0, 100) * 10);

        // Same shape as every confirmed burn stroke: a powered G1, then an unpowered G1 (here
        // zero-length) to drop the power before the next G0 travel.
        void LineX(double x0, double x1, double y)
        {
            sb.Append($"G0X{F(x0)}Y{F(y)}\n");
            sb.Append($"G1X{F(x1)}S{power}\n");
            sb.Append($"G1X{F(x1)}S0\n");
        }
        void LineY(double x, double y0, double y1)
        {
            sb.Append($"G0X{F(x)}Y{F(y0)}\n");
            sb.Append($"G1Y{F(y1)}S{power}\n");
            sb.Append($"G1Y{F(y1)}S0\n");
        }
        // A filled rectangle, rastered along its long side at the cell's line spacing.
        void Fill(double x0, double y0, double x1, double y1)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                if (x1 - x0 >= y1 - y0)
                    for (double y = y0 + lp / 2; y < y1; y += lp) LineX(x0, x1, y);
                else
                    for (double x = x0 + lp / 2; x < x1; x += lp) LineY(x, y0, y1);
            }
        }

        // The grid number as a row of dots between the two top crosses (CardId).
        foreach (var (x, y, d) in CardId.DotsMm(l, grid.Number, originX, originY))
            Fill(x - d / 2, y - d / 2, x + d / 2, y + d / 2);

        // Corner crosses: two filled bars each.
        double bar = Math.Min(MarkBarMm, half);
        foreach (var (cx, cy) in new[] { (tlx, tly - offset), (trx, tly - offset), (blx, bly + offset) })
        {
            Fill(cx - half, cy - bar / 2, cx + half, cy + bar / 2);
            Fill(cx - bar / 2, cy - half, cx + bar / 2, cy + half);
        }

        // "Grid 29" for people, in the bottom margin (GridLabel): each stroke filled to
        // MarkStrokeMm wide with parallel lines at the cell's spacing.
        foreach (var line in GridLabel.StrokesMm(l, grid.Number, originX, originY))
            for (int pass = 0; pass < passes; pass++)
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    var (ax, ay) = line[i];
                    var (bx, by) = line[i + 1];
                    double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    if (len <= 0) continue;
                    double nx = -(by - ay) / len, ny = (bx - ax) / len;   // unit normal
                    for (double o = -GridLabel.StrokeMm / 2 + lp / 2; o < GridLabel.StrokeMm / 2; o += lp)
                    {
                        sb.Append($"G0X{F(ax + nx * o)}Y{F(ay + ny * o)}\n");
                        sb.Append($"G1X{F(bx + nx * o)}Y{F(by + ny * o)}S{power}\n");
                        sb.Append($"G1X{F(bx + nx * o)}Y{F(by + ny * o)}S0\n");
                    }
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

    /// <summary>
    /// The camera calibration target (<see cref="MOPAChroma.Scanning.BedCameraTarget"/>): nine
    /// crosses at known bed positions, each with 1-9 bars beside it, drawn as vector strokes
    /// with the given settings (only power, speed, frequency and pulse width are used).
    /// Engrave it once on any scrap that marks, photograph it with the laser's camera, and
    /// click the crosses.
    /// </summary>
    public static string CalibrationTarget(MopaSettings s)
    {
        var sb = new StringBuilder();
        sb.Append(";wecreat 3.0.6\n");
        sb.Append(";canvas border: 0 0 210 210\n");
        sb.Append(Preamble);
        sb.Append($"M38F{(int)Math.Round(s.FrequencyKHz)}\n");
        sb.Append($"M39P{(int)Math.Round(s.PulseWidthNs)}\n");
        sb.Append("G0F15000\n");
        sb.Append($"G1F{(int)Math.Round(s.SpeedMmPerS * 60)}\n");
        int power = (int)Math.Round(Math.Clamp(s.PowerPercent, 0, 100) * 10);
        var pts = MOPAChroma.Scanning.BedCameraTarget.Points;
        const double arm = MOPAChroma.Scanning.BedCameraTarget.ArmMm;
        for (int i = 0; i < pts.Length; i++)
        {
            double cx = pts[i].X, cy = pts[i].Y;
            StrokeX(sb, cx - arm, cx + arm, cy, power);
            StrokeY(sb, cx, cy - arm, cy + arm, power);
            // i + 1 bars to the right of the cross (to the left on the right-hand column, to stay on the bed).
            double dir = cx > 150 ? -1 : 1;
            for (int b = 0; b <= i; b++)
            {
                double x = cx + dir * (MOPAChroma.Scanning.BedCameraTarget.BarGapMm + b * MOPAChroma.Scanning.BedCameraTarget.BarSpacingMm);
                double half = MOPAChroma.Scanning.BedCameraTarget.BarLengthMm / 2;
                StrokeY(sb, x, cy - half, cy + half, power);
            }
        }
        sb.Append(Postamble);
        return sb.ToString();
    }

    // Axis-aligned strokes in the confirmed pattern (powered G1, then a zero-length G1 at S0),
    // each three lines 0.05 mm apart so it reads clearly on any finish.
    private static void StrokeX(StringBuilder sb, double x0, double x1, double y, int power)
    {
        for (int k = -1; k <= 1; k++)
        {
            sb.Append($"G0X{F(x0)}Y{F(y + k * 0.05)}\n");
            sb.Append($"G1X{F(x1)}S{power}\n");
            sb.Append($"G1X{F(x1)}S0\n");
        }
    }

    private static void StrokeY(StringBuilder sb, double x, double y0, double y1, int power)
    {
        for (int k = -1; k <= 1; k++)
        {
            sb.Append($"G0X{F(x + k * 0.05)}Y{F(y0)}\n");
            sb.Append($"G1Y{F(y1)}S{power}\n");
            sb.Append($"G1Y{F(y1)}S0\n");
        }
    }

    private static void AppendCell(StringBuilder sb, GridCell cell, GridLayout l, double originX, double originY)
    {
        var s = cell.Settings;
        double pitch = s.LinePitchMm;
        if (double.IsNaN(pitch) || pitch <= 0) return; // no line density given - nothing to draw

        double x0 = originX + cell.Column * (l.CellSizeMm + l.GapMm);
        double y0 = originY + cell.Row * (l.CellSizeMm + l.GapMm);
        double margin = Math.Clamp(l.CellSizeMm * MarginFraction, 0, l.CellSizeMm / 2 - 0.001);
        double xStart = x0, xBurnStart = x0 + margin, xBurnEnd = x0 + l.CellSizeMm - margin, xEnd = x0 + l.CellSizeMm;

        int lines = Math.Max(1, (int)Math.Round(l.CellSizeMm / pitch));
        int power = (int)Math.Round(Math.Clamp(s.PowerPercent, 0, 100) * 10);
        int freq = (int)Math.Round(s.FrequencyKHz);
        int pulseWidthNs = (int)Math.Round(s.PulseWidthNs);
        int feed = (int)Math.Round(s.SpeedMmPerS * 60);

        sb.Append($"M38F{freq}\n");
        sb.Append($"M39P{pulseWidthNs}\n");
        sb.Append("G0F15000\n");
        sb.Append($"G1F{feed}\n");

        for (int pass = 0; pass < Math.Max(1, s.Passes); pass++)
        {
            for (int i = 0; i < lines; i++)
            {
                double y = y0 + i * pitch;
                sb.Append($"G0Y{F(y)}\n");
                sb.Append($"G0X{F(xStart)}\n");
                sb.Append($"G1X{F(xBurnStart)}S0\n");
                sb.Append($"G1X{F(xBurnEnd)}S{power}\n");
                sb.Append($"G1X{F(xEnd)}S0\n");
            }
        }
    }

    private static string F(double v) => v.ToString("0.###", Inv);
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
