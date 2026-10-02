using MOPAChroma.Laser;

namespace MOPAChroma.Planning;

/// <summary>A continuous range for one setting, sampled linearly or logarithmically.</summary>
public sealed record SettingRange(double Min, double Max, bool Log = false, double Step = 0)
{
    /// <summary>Map u in 0..1 into the SettingRange and snap to <see cref="Step"/>.</summary>
    public double At(double u)
    {
        double v = Log && Min > 0 && Max > 0
            ? Math.Exp(Math.Log(Min) + u * (Math.Log(Max) - Math.Log(Min)))
            : Min + u * (Max - Min);
        if (Step > 0) v = Math.Round(v / Step) * Step;
        return Math.Clamp(v, Math.Min(Min, Max), Math.Max(Min, Max));
    }
}

/// <summary>
/// The region of settings the planner may explore for one material. Deliberately
/// wide by default: the planner learns where the useful region is from the NoMark
/// and Damaged cells, rather than the user having to know in advance.
/// </summary>
public sealed record ParameterSpace
{
    public required SettingRange PowerPercent { get; init; }
    public required SettingRange SpeedMmPerS { get; init; }
    public required SettingRange LineDensityPerCm { get; init; }
    public required SettingRange FrequencyKHz { get; init; }
    public required IReadOnlyList<double> PulseWidthsNs { get; init; }
    public IReadOnlyList<int> Passes { get; init; } = [1];

    /// <summary>
    /// Largest pulse duty cycle (pulse width × frequency) considered valid. A pulse
    /// cannot outlast its own period; real sources cap long pulses well before that.
    /// </summary>
    public double MaxDutyCycle { get; init; } = 0.5;

    /// <summary>Heat per area above this is never planned (<see cref="HeatLimits"/>).</summary>
    public double MaxArealEnergyJPerMm2 { get; init; } = HeatLimits.AbsoluteMaxJPerMm2;

    /// <summary>Heat per area below this is not planned: it only leaves no mark (per material, <see cref="Atlas.PlanningRanges"/>).</summary>
    public double MinArealEnergyJPerMm2 { get; init; }

    /// <summary>Settings leaving gaps wider than this fraction of a spot are skipped.</summary>
    public double MinPulseOverlap { get; init; } = -0.5;

    /// <summary>
    /// A starting space for colour work with a given source. The ranges are a broad
    /// first guess, to be narrowed per material once the first grids come back.
    /// </summary>
    public static ParameterSpace DefaultFor(SourceProfile source) => new()
    {
        PowerPercent = new SettingRange(5, 90, Step: 1),
        SpeedMmPerS = new SettingRange(50, Math.Min(3000, source.SpeedMaxMmPerS), Log: true, Step: 10),
        // Up to 3000 lines/cm (0.0033 mm apart) and three passes: thicker oxides (purples,
        // blues and beyond) need more heat per area than single passes at wide spacing give.
        LineDensityPerCm = new SettingRange(150, 3000, Log: true, Step: 10),
        FrequencyKHz = new SettingRange(Math.Max(20, source.FrequencyMinKHz), Math.Min(1500, source.FrequencyMaxKHz), Log: true, Step: 1),
        PulseWidthsNs = source.PulseWidthsNs.Where(w => w >= 4).ToList(),
        Passes = [1, 2, 3],
    };

    /// <summary>
    /// The space for one material: its <see cref="Atlas.PlanningRanges"/>, clipped to what the
    /// source can do. Pulse widths are the source's own values inside the range (if none fall
    /// inside, the one nearest the range).
    /// </summary>
    public static ParameterSpace For(SourceProfile source, Atlas.PlanningRanges? ranges)
    {
        var r = ranges ?? Atlas.PlanningRanges.Default;
        static (double, double) Order(double a, double b) => a <= b ? (a, b) : (b, a);
        var (pLo, pHi) = Order(r.PowerMinPercent, r.PowerMaxPercent);
        var (fLo, fHi) = Order(r.FrequencyMinKHz, r.FrequencyMaxKHz);
        var (wLo, wHi) = Order(r.PulseWidthMinNs, r.PulseWidthMaxNs);
        var (vLo, vHi) = Order(r.SpeedMinMmPerS, r.SpeedMaxMmPerS);
        var (dLo, dHi) = Order(r.LineDensityMinPerCm, r.LineDensityMaxPerCm);
        fLo = Math.Max(fLo, source.FrequencyMinKHz); fHi = Math.Min(fHi, source.FrequencyMaxKHz);
        if (source.SpeedMaxMmPerS > 0) vHi = Math.Min(vHi, source.SpeedMaxMmPerS);
        var widths = source.PulseWidthsNs.Where(w => w >= wLo - 1e-6 && w <= wHi + 1e-6).ToList();
        if (widths.Count == 0 && source.PulseWidthsNs.Count > 0)
            widths = [source.PulseWidthsNs.MinBy(w => Math.Min(Math.Abs(w - wLo), Math.Abs(w - wHi)))];
        return new ParameterSpace
        {
            PowerPercent = new SettingRange(Math.Clamp(pLo, 0, 100), Math.Clamp(pHi, 0, 100), Step: 1),
            SpeedMmPerS = new SettingRange(Math.Max(1, vLo), Math.Max(1, vHi), Log: true, Step: 10),
            LineDensityPerCm = new SettingRange(Math.Max(10, dLo), Math.Max(10, dHi), Log: true, Step: 10),
            FrequencyKHz = new SettingRange(fLo, Math.Max(fLo, fHi), Log: true, Step: 1),
            PulseWidthsNs = widths,
            Passes = Enumerable.Range(1, Math.Clamp(r.MaxPasses, 1, Atlas.PlanningRanges.MostPasses)).ToList(),
            MinArealEnergyJPerMm2 = Math.Clamp(r.HeatMinJPerMm2, 0, HeatLimits.AbsoluteMaxJPerMm2 / 2),
        };
    }

    /// <summary>Map a point of the unit hypercube (6 coordinates) to snapped settings.</summary>
    public MopaSettings At(ReadOnlySpan<double> u) => new()
    {
        PowerPercent = PowerPercent.At(u[0]),
        SpeedMmPerS = SpeedMmPerS.At(u[1]),
        LineDensityPerCm = LineDensityPerCm.At(u[2]),
        FrequencyKHz = FrequencyKHz.At(u[3]),
        PulseWidthNs = Pick(PulseWidthsNs, u[4]),
        Passes = Pick(Passes, u[5]),
    };

    public bool IsValid(MopaSettings s, Physics p) =>
        s.FrequencyKHz * 1e3 * s.PulseWidthNs * 1e-9 <= MaxDutyCycle
        && p.PulseOverlap >= MinPulseOverlap
        && double.IsFinite(p.ArealEnergyJPerMm2)
        && p.ArealEnergyJPerMm2 <= MaxArealEnergyJPerMm2
        && p.ArealEnergyJPerMm2 >= MinArealEnergyJPerMm2;

    private static T Pick<T>(IReadOnlyList<T> list, double u) =>
        list[Math.Clamp((int)(u * list.Count), 0, list.Count - 1)];
}
