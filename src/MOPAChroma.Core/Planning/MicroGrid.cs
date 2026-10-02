using MOPAChroma.Grids;
using MOPAChroma.Laser;

namespace MOPAChroma.Planning;

/// <summary>
/// Plans a small card centred on one promising cell - level 3 of the field guide's funnel.
/// Everything but the two swept settings is locked to the seed cell (pulse width, line
/// density, passes, Z and the third of power/speed/frequency).
///
/// Ranges around the seed, from the Lumos Ultra MOPA Color Engraving Field Guide:
/// power ±5 percentage points, speed ±22.5 % (the guide says 20-25 %). Frequency is not
/// covered by the guide; ±20 % is used as a starting point.
///
/// With odd row and column counts the centre cell repeats the seed's settings on a fresh
/// blank, so the micro-grid also serves as the seed's repeatability check.
/// </summary>
public static class MicroGrid
{
    public const double PowerSpanPoints = 5;
    public const double SpeedSpanFraction = 0.225;
    public const double FrequencySpanFraction = 0.20;

    /// <param name="seed">Settings of the cell to centre on.</param>
    /// <param name="parent">The card the seed came from, if it was a MakeIt test card: its axes and directions are kept.</param>
    /// <param name="source">The laser source; its limits clip the ranges.</param>
    /// <param name="rows">Rows on the new card. Odd, so the centre row is the seed.</param>
    /// <param name="columns">Columns on the new card. Odd, so the centre column is the seed.</param>
    public static ColorTestDefinition Around(MopaSettings seed, ColorTestDefinition? parent, SourceProfile source,
                                             int rows = 5, int columns = 9)
    {
        var rowAxis = parent?.RowSweep.Axis ?? CardAxis.Speed;
        var colAxis = parent?.ColumnSweep.Axis ?? CardAxis.Power;
        bool rowDesc = parent?.RowSweep.Descending ?? true;
        bool colDesc = parent?.ColumnSweep.Descending ?? false;

        return new ColorTestDefinition
        {
            RowSweep = Sweep(rowAxis, rows, rowDesc),
            ColumnSweep = Sweep(colAxis, columns, colDesc),
            PowerPercent = seed.PowerPercent,
            SpeedMmPerS = seed.SpeedMmPerS,
            FrequencyKHz = seed.FrequencyKHz,
            PulseWidthNs = seed.PulseWidthNs,
            LineDensity = seed.LineDensityPerCm,
            Passes = seed.Passes,
            FocusZMm = seed.FocusOffsetMm,
        };

        AxisSweep Sweep(CardAxis a, int n, bool desc)
        {
            var (lo, hi) = a switch
            {
                // Ends rounded so they can be typed into MakeIt; the centre stays within half
                // a rounding step of the seed (e.g. seed 42.63 % → 37.6-47.6, centre 42.6).
                CardAxis.Power => Symmetric(seed.PowerPercent, PowerSpanPoints, 1, 100, 0.1),
                CardAxis.Speed => Symmetric(seed.SpeedMmPerS, seed.SpeedMmPerS * SpeedSpanFraction, 1,
                                            source.SpeedMaxMmPerS > 0 ? source.SpeedMaxMmPerS : double.MaxValue, 1),
                _ => Symmetric(seed.FrequencyKHz, seed.FrequencyKHz * FrequencySpanFraction,
                               Math.Max(1, source.FrequencyMinKHz),
                               source.FrequencyMaxKHz > 0 ? source.FrequencyMaxKHz : double.MaxValue, 1),
            };
            return new AxisSweep(a, lo, hi, n, desc);
        }
    }

    /// <summary>
    /// centre ± span, rounded to <paramref name="step"/>, clipped to [min, max]. When a limit
    /// clips one side the other side is pulled in too, so the seed stays in the middle.
    /// </summary>
    private static (double lo, double hi) Symmetric(double centre, double span, double min, double max, double step)
    {
        span = Math.Min(span, Math.Min(centre - min, max - centre));
        span = Math.Max(0, Math.Floor(span / step) * step);
        if (span <= 0) span = step; // degenerate seed at a limit: still give a tiny sweep
        return (Math.Round((centre - span) / step) * step, Math.Round((centre + span) / step) * step);
    }

    /// <summary>
    /// The cells of a micro-grid as an Atlas grid (sent to the laser like any other): a
    /// sweep of power and speed around <paramref name="seed"/>, with everything else locked.
    /// The seed itself appears once, as a deliberate repeat (repeatability check); no other
    /// cell repeats anything already engraved (<paramref name="alreadyTested"/>, compared at
    /// MakeIt's typing precision), and cells nearest the seed come first when the card holds
    /// fewer than the full sweep.
    /// </summary>
    public static IReadOnlyList<(MopaSettings Settings, string Reason)> AtlasCells(
        MopaSettings seed, SourceProfile source, IEnumerable<MopaSettings> alreadyTested, int capacity, int rows = 5, int columns = 9,
        Func<MopaSettings, bool>? allowed = null)
    {
        var def = Around(seed, null, source, rows, columns);
        var tested = alreadyTested.ToList();
        var picks = new List<(MopaSettings Settings, string Reason, double Distance)>
        {
            (seed, "Repeat of the chosen cell on a new card: checks its colour repeats", -1),
        };
        for (int r = 0; r < def.Rows; r++)
            for (int c = 0; c < def.Columns; c++)
            {
                var s = def.SettingsAt(r, c) with { HatchAngleDeg = seed.HatchAngleDeg };
                if (Atlas.Repeatability.SameSettings(s, seed)) continue;
                if (allowed is not null && !allowed(s)) continue; // e.g. above the heat ceiling (HeatLimits)
                if (tested.Any(t => Atlas.Repeatability.SameSettings(t, s))) continue;
                if (picks.Any(p => Atlas.Repeatability.SameSettings(p.Settings, s))) continue;
                double dp = s.PowerPercent - seed.PowerPercent;
                double ds = (s.SpeedMmPerS / seed.SpeedMmPerS - 1) * 100;
                double d = dp / PowerSpanPoints * (dp / PowerSpanPoints) + ds / (SpeedSpanFraction * 100) * (ds / (SpeedSpanFraction * 100));
                picks.Add((s, $"Around the chosen cell: power {dp:+0.#;-0.#;0} points, speed {ds:+0;-0;0} %", d));
            }
        return picks.OrderBy(p => p.Distance).Take(Math.Max(1, capacity)).Select(p => (p.Settings, p.Reason)).ToList();
    }
}
