using System.Globalization;
using System.Text;

namespace LumosUltra.Jobs;

/// <summary>
/// Writes a WeCreat Lumos Ultra job file (the gcode.gc MakeIt stages and the machine runs)
/// directly, so a program can engrave exactly what it planned without MakeIt's own tools in
/// the way. Everything here is in the shape CONFIRMED against a real Lumos Ultra:
/// <list type="bullet">
/// <item>The setup preamble and shutdown postamble are copied verbatim from a real
/// MakeIt-generated job. They are machine initialisation and homing sequences not otherwise
/// understood command by command; a job that used exactly this preamble and postamble ran
/// correctly, laser included, on the authors' own machine.</item>
/// <item>Per stroke: power is <c>S = percent × 10</c> on the burn move, frequency
/// <c>M38F&lt;kHz&gt;</c>, pulse width <c>M39P&lt;ns&gt;</c>, speed <c>G1F&lt;mm/s × 60&gt;</c>
/// (mm/min), and every powered <c>G1</c> is followed by an unpowered <c>G1</c> (often of zero
/// length) that drops the power before the next travel. This encoding was cross-checked
/// against a MakeIt colour-test job and the physical part it engraved
/// (MOPAChroma Atlas, docs/CALIBRATION-FINDINGS.md).</item>
/// <item>The bed is a 210 × 210 mm canvas and MakeIt's home (the canvas centre) is X 105,
/// Y 105; Y grows downwards as on MakeIt's canvas.</item>
/// </list>
/// Z moves (<c>G0Z&lt;mm&gt;</c>, absolute, seen in MakeIt relief jobs descending 10 µm per
/// layer) are offered by <see cref="MoveZ"/> but their reference height is the one the
/// machine set when it focused, which a job cannot know on its own: use them only with a
/// height read from the machine, never a guessed one.
/// </summary>
public sealed class MakeItJob
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Side of MakeIt's canvas, mm (<c>;canvas border: 0 0 210 210</c>).</summary>
    public const double BedMm = 210;

    /// <summary>MakeIt's home, the canvas centre, mm.</summary>
    public const double HomeX = 105, HomeY = 105;

    /// <summary>Machine-specific setup sequence, copied verbatim from a real MakeIt job
    /// capture. Not independently understood command-by-command; confirmed to work.</summary>
    public const string Preamble =
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
    public const string Postamble =
        "G0X105Y105\n" +
        "M9\n" +
        "G1 S0\n" +
        "M5\n" +
        "G90\n" +
        "M6\n";

    private readonly StringBuilder _sb = new();
    private bool _finished;

    /// <summary>Starts a job: the header comments MakeIt writes, then the preamble.</summary>
    public MakeItJob(string makeItVersion = "3.0.6")
    {
        _sb.Append(";wecreat ").Append(makeItVersion).Append('\n');
        _sb.Append(";canvas border: 0 0 210 210\n");
        _sb.Append(Preamble);
    }

    /// <summary>The power word for a percentage: S 0..1000.</summary>
    public static int PowerS(double percent) => (int)Math.Round(Math.Clamp(percent, 0, 100) * 10);

    /// <summary>Number formatting every coordinate uses: up to three decimals, invariant.</summary>
    public static string F(double v) => v.ToString("0.###", Inv);

    /// <summary>Characters written so far.</summary>
    public int Length => _sb.Length;

    // ---------------------------------------------------------------- settings

    /// <summary>Frequency (kHz) and pulse width (ns) for the strokes that follow.</summary>
    public MakeItJob Pulse(double frequencyKHz, double pulseWidthNs)
    {
        _sb.Append("M38F").Append((int)Math.Round(frequencyKHz)).Append('\n');
        _sb.Append("M39P").Append((int)Math.Round(pulseWidthNs)).Append('\n');
        return this;
    }

    /// <summary>Travel feed (the fixed 15000 MakeIt writes) and the burn speed, mm/s, for the strokes that follow.</summary>
    public MakeItJob Feed(double speedMmPerS)
    {
        _sb.Append("G0F15000\n");
        _sb.Append("G1F").Append((int)Math.Round(speedMmPerS * 60)).Append('\n');
        return this;
    }

    /// <summary>Absolute Z, mm. See the class notes before using it.</summary>
    public MakeItJob MoveZ(double zMm)
    {
        _sb.Append("G0Z").Append(F(zMm)).Append('\n');
        return this;
    }

    // ---------------------------------------------------------------- strokes

    /// <summary>Laser-off travel.</summary>
    public MakeItJob Travel(double x, double y)
    {
        _sb.Append("G0X").Append(F(x)).Append('Y').Append(F(y)).Append('\n');
        return this;
    }

    /// <summary>A horizontal stroke in the confirmed pattern: travel to the start, a powered
    /// move to the end, then a zero-length unpowered move.</summary>
    public MakeItJob LineX(double x0, double x1, double y, int powerS)
    {
        _sb.Append("G0X").Append(F(x0)).Append('Y').Append(F(y)).Append('\n');
        _sb.Append("G1X").Append(F(x1)).Append('S').Append(powerS).Append('\n');
        _sb.Append("G1X").Append(F(x1)).Append("S0\n");
        return this;
    }

    /// <summary>A vertical stroke, as <see cref="LineX"/>.</summary>
    public MakeItJob LineY(double x, double y0, double y1, int powerS)
    {
        _sb.Append("G0X").Append(F(x)).Append('Y').Append(F(y0)).Append('\n');
        _sb.Append("G1Y").Append(F(y1)).Append('S').Append(powerS).Append('\n');
        _sb.Append("G1Y").Append(F(y1)).Append("S0\n");
        return this;
    }

    /// <summary>A stroke between any two points, as <see cref="LineX"/>.</summary>
    public MakeItJob Line(double ax, double ay, double bx, double by, int powerS)
    {
        _sb.Append("G0X").Append(F(ax)).Append('Y').Append(F(ay)).Append('\n');
        _sb.Append("G1X").Append(F(bx)).Append('Y').Append(F(by)).Append('S').Append(powerS).Append('\n');
        _sb.Append("G1X").Append(F(bx)).Append('Y').Append(F(by)).Append("S0\n");
        return this;
    }

    /// <summary>
    /// A filled rectangle, rastered along its long side with lines <paramref name="linePitchMm"/>
    /// apart, the whole thing repeated <paramref name="passes"/> times.
    /// </summary>
    public MakeItJob Fill(double x0, double y0, double x1, double y1, double linePitchMm, int powerS, int passes = 1)
    {
        if (!(linePitchMm > 0)) linePitchMm = 0.05;
        for (int pass = 0; pass < Math.Max(1, passes); pass++)
        {
            if (x1 - x0 >= y1 - y0)
                for (double y = y0 + linePitchMm / 2; y < y1; y += linePitchMm) LineX(x0, x1, y, powerS);
            else
                for (double x = x0 + linePitchMm / 2; x < x1; x += linePitchMm) LineY(x, y0, y1, powerS);
        }
        return this;
    }

    /// <summary>
    /// A stroke of a given width between two points, filled with parallel lines
    /// <paramref name="linePitchMm"/> apart (so an engraved letter marks the way a filled
    /// cell does, not as one thin line).
    /// </summary>
    public MakeItJob Band(double ax, double ay, double bx, double by, double widthMm, double linePitchMm, int powerS, int passes = 1)
    {
        double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        if (len <= 0) return this;
        if (!(linePitchMm > 0)) linePitchMm = 0.05;
        double nx = -(by - ay) / len, ny = (bx - ax) / len;   // unit normal
        for (int pass = 0; pass < Math.Max(1, passes); pass++)
            for (double o = -widthMm / 2 + linePitchMm / 2; o < widthMm / 2; o += linePitchMm)
                Line(ax + nx * o, ay + ny * o, bx + nx * o, by + ny * o, powerS);
        return this;
    }

    /// <summary>
    /// One square test cell rastered in MakeIt's own way: every scan line travels to the
    /// cell's left edge, flies in unpowered over a margin, burns across, and flies out over the
    /// same margin, so the laser is already at speed where the burn starts and stops (the
    /// zero-power lead-in and lead-out seen on real MakeIt cells).
    /// </summary>
    public MakeItJob RasterCell(double x0, double y0, double sizeMm, double linePitchMm, int powerS, int passes = 1, double marginFraction = 0.1)
    {
        if (!(linePitchMm > 0)) return this;
        double margin = Math.Clamp(sizeMm * marginFraction, 0, sizeMm / 2 - 0.001);
        double xStart = x0, xBurnStart = x0 + margin, xBurnEnd = x0 + sizeMm - margin, xEnd = x0 + sizeMm;
        int lines = Math.Max(1, (int)Math.Round(sizeMm / linePitchMm));
        for (int pass = 0; pass < Math.Max(1, passes); pass++)
            for (int i = 0; i < lines; i++)
            {
                double y = y0 + i * linePitchMm;
                _sb.Append("G0Y").Append(F(y)).Append('\n');
                _sb.Append("G0X").Append(F(xStart)).Append('\n');
                _sb.Append("G1X").Append(F(xBurnStart)).Append("S0\n");
                _sb.Append("G1X").Append(F(xBurnEnd)).Append('S').Append(powerS).Append('\n');
                _sb.Append("G1X").Append(F(xEnd)).Append("S0\n");
            }
        return this;
    }

    /// <summary>
    /// A rectangle hatched at any angle: parallel lines <paramref name="linePitchMm"/> apart at
    /// <paramref name="angleDeg"/> from the X axis (0 = along X, 90 = along Y), each clipped
    /// to the rectangle, alternating direction so the head need not fly back. With an unpowered
    /// lead-in and lead-out of <paramref name="leadMm"/> beyond the rectangle on every line (the
    /// MakeIt pattern), so the burn starts and stops at speed; keep it 0 when the margin matters.
    /// For angles that are not a multiple of 90 the lead runs along the line's own direction.
    /// </summary>
    public MakeItJob Hatch(double x0, double y0, double x1, double y1, double linePitchMm, double angleDeg, int powerS, double leadMm = 0)
    {
        if (!(linePitchMm > 0) || x1 <= x0 || y1 <= y0) return this;
        double a = angleDeg * Math.PI / 180;
        double dx = Math.Cos(a), dy = Math.Sin(a);      // along the line
        double nx = -dy, ny = dx;                       // across the lines
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        double half = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) / 2;
        int n = (int)Math.Floor(half * 2 / linePitchMm);
        double start = -((n - 1) * linePitchMm) / 2;
        bool forward = true;
        for (int i = 0; i < n; i++)
        {
            double o = start + i * linePitchMm;
            double px = cx + nx * o, py = cy + ny * o;
            // Clip the line p + t·d to the rectangle (Liang-Barsky).
            double tin = -half, tout = half;
            if (!Clip(px, py, dx, dy, x0, y0, x1, y1, ref tin, ref tout)) continue;
            double ax = px + dx * tin, ay = py + dy * tin, bx = px + dx * tout, by = py + dy * tout;
            double lx = px + dx * (tin - leadMm), ly = py + dy * (tin - leadMm), mx = px + dx * (tout + leadMm), my = py + dy * (tout + leadMm);
            if (!forward) { (ax, ay, bx, by) = (bx, by, ax, ay); (lx, ly, mx, my) = (mx, my, lx, ly); }
            forward = !forward;
            if (leadMm > 0)
            {
                _sb.Append("G0X").Append(F(lx)).Append('Y').Append(F(ly)).Append('\n');
                _sb.Append("G1X").Append(F(ax)).Append('Y').Append(F(ay)).Append("S0\n");
                _sb.Append("G1X").Append(F(bx)).Append('Y').Append(F(by)).Append('S').Append(powerS).Append('\n');
                _sb.Append("G1X").Append(F(mx)).Append('Y').Append(F(my)).Append("S0\n");
            }
            else Line(ax, ay, bx, by, powerS);
        }
        return this;
    }

    private static bool Clip(double px, double py, double dx, double dy, double x0, double y0, double x1, double y1, ref double tin, ref double tout)
    {
        double t0 = double.NegativeInfinity, t1 = double.PositiveInfinity;
        if (!Edge(-dx, px - x0, ref t0, ref t1) || !Edge(dx, x1 - px, ref t0, ref t1)
            || !Edge(-dy, py - y0, ref t0, ref t1) || !Edge(dy, y1 - py, ref t0, ref t1)) return false;
        if (t1 - t0 < 1e-9) return false;
        tin = t0; tout = t1;
        return true;
    }

    private static bool Edge(double p, double q, ref double t0, ref double t1)
    {
        if (Math.Abs(p) < 1e-12) return q >= 0;
        double t = q / p;
        if (p < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
        else { if (t < t0) return false; if (t < t1) t1 = t; }
        return true;
    }

    /// <summary>Any raw line, for a command this class has no method for.</summary>
    public MakeItJob Raw(string line)
    {
        _sb.Append(line);
        if (!line.EndsWith('\n')) _sb.Append('\n');
        return this;
    }

    // ---------------------------------------------------------------- end

    /// <summary>Appends the postamble (once) and returns the whole job.</summary>
    public string Finish()
    {
        if (!_finished) { _sb.Append(Postamble); _finished = true; }
        return _sb.ToString();
    }

    /// <summary>The job so far, without the postamble unless <see cref="Finish"/> was called.</summary>
    public override string ToString() => _sb.ToString();
}
