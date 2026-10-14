using System.Globalization;

namespace LumosUltra.Jobs;

/// <summary>
/// An independent check of a finished job file: how much energy every square millimetre of the
/// bed would receive. It reads only the G-code that is about to be sent (every powered G1 move:
/// its power S, feed F and path) and adds up power × time over the area each move crosses, so
/// passes, line spacing, speed and power all count, however the job was made. It trusts nothing
/// the planner says.
///
/// Written after 2026-10-01, when planned cells with three passes at very close line spacing cut
/// through a stainless sheet and marked the machine's plate. Every job Atlas sends to the laser
/// goes through <see cref="Check"/>; a job that fails is never sent.
///
/// The bed is divided into 1 mm squares. A raster line contributes P · L / v joules to the
/// squares it crosses (P from S: S is power × 10 of the source's rated power, as MakeIt writes it;
/// v from F in mm/min). With lines a pitch p apart, a square holds about 1/p of them, so its
/// total is P / (v · p) per pass: the same heat per area the planner uses. The count of lines
/// falling in one square can be off by one, which makes this read up to one line pitch high
/// (under 7 % at the widest spacing Atlas plans), so the limit sits a little above the planner's.
/// </summary>
public static class JobAudit
{
    /// <summary>
    /// Most energy any 1 mm² may receive, J/mm². The planner's ceiling (Planning.HeatLimits,
    /// 40 J/mm²) plus room for the measurement's own rounding, and still several times below
    /// what cut through the sheet.
    /// </summary>
    public const double MaxJPerMm2 = 45;

    private const double Cell = 1.0;   // mm

    public sealed record Result(double MaxJPerMm2, double AtXMm, double AtYMm, double TotalJoules, bool Ok, string Message);

    /// <summary>Audits <paramref name="gcode"/> for a source of <paramref name="ratedPowerW"/>.</summary>
    public static Result Check(string gcode, double ratedPowerW, double limit = MaxJPerMm2)
    {
        if (!(ratedPowerW > 0)) return new(double.NaN, 0, 0, 0, false, "Unknown laser power, so the job cannot be checked and was not sent.");
        var grid = new Dictionary<(int, int), double>();
        double x = 0, y = 0, f = 0, s = 0, total = 0;
        bool unreadable = false;
        foreach (var raw in gcode.Split('\n'))
        {
            var line = raw.Trim();
            int sc = line.IndexOf(';');
            if (sc >= 0) line = line[..sc];
            if (line.Length == 0 || (line[0] != 'G' && line[0] != 'g')) continue;
            int g = Code(line);
            if (g is not (0 or 1)) continue;
            double nx = Word(line, 'X') ?? x, ny = Word(line, 'Y') ?? y;
            if (Word(line, 'F') is { } nf) f = nf;
            if (Word(line, 'S') is { } ns) s = ns;
            if (g == 1 && s > 0)
            {
                double len = Math.Sqrt((nx - x) * (nx - x) + (ny - y) * (ny - y));
                if (len > 0)
                {
                    if (!(f > 0)) { unreadable = true; break; }
                    double p = ratedPowerW * Math.Clamp(s, 0, 1000) / 1000.0;   // W
                    double joules = p * len / (f / 60.0);
                    total += joules;
                    // Split the move exactly where it crosses square edges; each piece's share
                    // of the energy goes to the square it lies in.
                    var cuts = new List<double> { 0, 1 };
                    AddCrossings(cuts, x, nx);
                    AddCrossings(cuts, y, ny);
                    cuts.Sort();
                    for (int k = 0; k + 1 < cuts.Count; k++)
                    {
                        double t0 = cuts[k], t1 = cuts[k + 1];
                        if (t1 - t0 <= 1e-12) continue;
                        double tm = (t0 + t1) / 2;
                        var key = ((int)Math.Floor((x + (nx - x) * tm) / Cell), (int)Math.Floor((y + (ny - y) * tm) / Cell));
                        grid[key] = grid.GetValueOrDefault(key) + joules * (t1 - t0);
                    }
                }
            }
            x = nx; y = ny;
        }
        if (unreadable)
            return new(double.NaN, x, y, total, false, "The job turns the laser on without a feed rate, so it cannot be checked and was not sent.");
        if (grid.Count == 0) return new(0, 0, 0, 0, true, "The job does not fire the laser.");

        var (key0, worst) = grid.MaxBy(kv => kv.Value);
        double perMm2 = worst / (Cell * Cell);
        double ax = (key0.Item1 + 0.5) * Cell, ay = (key0.Item2 + 0.5) * Cell;
        bool ok = perMm2 <= limit;
        string msg = ok
            ? $"Heat check passed: at most {perMm2:0.#} J/mm² anywhere (limit {limit:0})."
            : $"Not sent: the job would put {perMm2:0} J/mm² into the metal around X {ax:0} Y {ay:0} mm, over the safe limit of {limit:0} J/mm². " +
              "That much heat cuts into or through the blank.";
        return new(perMm2, ax, ay, total, ok, msg);
    }

    /// <summary>Fractions t in (0, 1) at which a move from a to b crosses a square edge.</summary>
    private static void AddCrossings(List<double> cuts, double a, double b)
    {
        if (a == b) return;
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        for (double e = Math.Floor(lo / Cell) * Cell + Cell; e < hi; e += Cell)
            cuts.Add((e - a) / (b - a));
    }

    private static int Code(string line)
    {
        int j = 1;
        while (j < line.Length && char.IsDigit(line[j])) j++;
        return j > 1 && int.TryParse(line[1..j], out int c) ? c : -1;
    }

    private static double? Word(string line, char letter)
    {
        for (int i = 1; i < line.Length; i++)
        {
            if (char.ToUpperInvariant(line[i]) != letter) continue;
            int j = i + 1;
            while (j < line.Length && (char.IsDigit(line[j]) || line[j] is '.' or '-' or '+')) j++;
            if (j > i + 1 && double.TryParse(line[(i + 1)..j], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return v;
        }
        return null;
    }
}
