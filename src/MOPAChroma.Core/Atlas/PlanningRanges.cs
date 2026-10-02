namespace MOPAChroma.Atlas;

/// <summary>
/// The settings the planner may use for one material: everything it plans stays inside these
/// ranges (and under the heat limits, Planning.HeatLimits, which always apply on top). Set per
/// material in the main window, so what you already know about a material keeps every test
/// cell in colour-making territory instead of spreading cells across settings that only leave
/// no mark or cut.
///
/// Defaults are Lee's starting ranges for colour on stainless with the Lumos Ultra's MOPA source
/// (2026-10-01): frequency 140-400 kHz, power 30-90 %, pulse width 4-7 ns. Speed and line density
/// reach MakeIt's own solid-colour test (5000 mm/s, 5000 lines/cm, 6 ns: 4-9 J/mm²).
/// </summary>
public sealed record PlanningRanges
{
    public double PowerMinPercent { get; init; } = 30;
    public double PowerMaxPercent { get; init; } = 90;
    public double FrequencyMinKHz { get; init; } = 140;
    public double FrequencyMaxKHz { get; init; } = 400;
    /// <summary>Pulse widths the source offers within this span are used.</summary>
    public double PulseWidthMinNs { get; init; } = 4;
    public double PulseWidthMaxNs { get; init; } = 7;
    public double SpeedMinMmPerS { get; init; } = 50;
    /// <summary>Up to 5000 mm/s: MakeIt's own solid-colour test runs at 5000 mm/s (F300000 in its G-code, 2026-09-27).</summary>
    public double SpeedMaxMmPerS { get; init; } = 5000;
    public double LineDensityMinPerCm { get; init; } = 150;
    /// <summary>Up to 5000 lines/cm (0.002 mm apart), MakeIt's solid-colour line density.</summary>
    public double LineDensityMaxPerCm { get; init; } = 5000;
    /// <summary>Most passes per cell: 1, 2 or 3 (<see cref="MostPasses"/>).</summary>
    public int MaxPasses { get; init; } = 3;

    /// <summary>The most passes the planner ever uses on a cell.</summary>
    public const int MostPasses = 3;
    /// <summary>
    /// Least heat per area (J/mm²) worth a cell. Below about 0.5 J/mm² stainless left no mark on
    /// the 2026-10-01 grid; other metals may colour with less, so it is set per material.
    /// </summary>
    public double HeatMinJPerMm2 { get; init; } = 0.5;

    public static PlanningRanges Default { get; } = new();

    /// <summary>True when these settings lie inside the ranges (a small tolerance for rounding).</summary>
    public bool Contains(Laser.MopaSettings s) =>
        s.PowerPercent >= PowerMinPercent - 0.5 && s.PowerPercent <= PowerMaxPercent + 0.5
        && s.FrequencyKHz >= FrequencyMinKHz - 0.5 && s.FrequencyKHz <= FrequencyMaxKHz + 0.5
        && s.PulseWidthNs >= PulseWidthMinNs - 1e-6 && s.PulseWidthNs <= PulseWidthMaxNs + 1e-6
        && s.SpeedMmPerS >= SpeedMinMmPerS - 0.5 && s.SpeedMmPerS <= SpeedMaxMmPerS + 0.5
        && s.LineDensityPerCm >= LineDensityMinPerCm - 0.5 && s.LineDensityPerCm <= LineDensityMaxPerCm + 0.5
        && Math.Max(1, s.Passes) <= Math.Clamp(MaxPasses, 1, MostPasses);

    /// <summary>
    /// These ranges stretched just far enough to hold <paramref name="s"/>. A cell that already
    /// made colour outside the ranges is evidence the ranges are too narrow there, so a
    /// micro-grid around it may sweep from it towards the ranges. Passes are never stretched
    /// beyond <see cref="MostPasses"/>; the heat limits still apply on top.
    /// </summary>
    public PlanningRanges Including(Laser.MopaSettings s) => this with
    {
        PowerMinPercent = Math.Min(PowerMinPercent, s.PowerPercent),
        PowerMaxPercent = Math.Max(PowerMaxPercent, s.PowerPercent),
        FrequencyMinKHz = Math.Min(FrequencyMinKHz, s.FrequencyKHz),
        FrequencyMaxKHz = Math.Max(FrequencyMaxKHz, s.FrequencyKHz),
        PulseWidthMinNs = Math.Min(PulseWidthMinNs, s.PulseWidthNs),
        PulseWidthMaxNs = Math.Max(PulseWidthMaxNs, s.PulseWidthNs),
        SpeedMinMmPerS = Math.Min(SpeedMinMmPerS, s.SpeedMmPerS),
        SpeedMaxMmPerS = Math.Max(SpeedMaxMmPerS, s.SpeedMmPerS),
        LineDensityMinPerCm = Math.Min(LineDensityMinPerCm, s.LineDensityPerCm),
        LineDensityMaxPerCm = Math.Max(LineDensityMaxPerCm, s.LineDensityPerCm),
        MaxPasses = Math.Clamp(Math.Max(MaxPasses, s.Passes), 1, MostPasses),
    };

    public string Summary =>
        $"power {PowerMinPercent:0}-{PowerMaxPercent:0} %, frequency {FrequencyMinKHz:0}-{FrequencyMaxKHz:0} kHz, " +
        $"pulse {PulseWidthMinNs:0.#}-{PulseWidthMaxNs:0.#} ns, speed {SpeedMinMmPerS:0}-{SpeedMaxMmPerS:0} mm/s, " +
        $"lines {LineDensityMinPerCm:0}-{LineDensityMaxPerCm:0}/cm, up to {MaxPasses} pass{(MaxPasses == 1 ? "" : "es")}, " +
        $"at least {HeatMinJPerMm2:0.##} J/mm²";
}
