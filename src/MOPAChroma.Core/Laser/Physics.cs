namespace MOPAChroma.Laser;

/// <summary>
/// The physically meaningful quantities behind a set of settings.
///
/// Why this exists: MOPA colour on metal is a thin oxide film, and its thickness is
/// set by how much heat each spot receives and how fast. Many different setting
/// combinations deliver the same heat in the same way — double the power and double
/// the speed and very little changes. A grid that steps each knob independently
/// burns most of its cells on those duplicates. The planner measures distance
/// between candidates in THIS space, so two cells are only both engraved when they
/// are physically different.
/// </summary>
public readonly record struct Physics
{
    /// <summary>Average optical power, W. Assumes output scales linearly with the power percentage.</summary>
    public double AveragePowerW { get; init; }

    /// <summary>Energy per pulse, mJ (= W / kHz).</summary>
    public double PulseEnergyMJ { get; init; }

    /// <summary>Approximate peak power, kW, treating the pulse as rectangular.</summary>
    public double PeakPowerKW { get; init; }

    /// <summary>Peak fluence per pulse, J/cm², over the nominal spot area.</summary>
    public double FluenceJPerCm2 { get; init; }

    /// <summary>Distance between successive pulses along a line, µm (= mm/s ÷ kHz).</summary>
    public double PulsePitchUm { get; init; }

    /// <summary>Fraction of the spot overlapped by the next pulse. Negative means gaps.</summary>
    public double PulseOverlap { get; init; }

    /// <summary>Fraction of the spot overlapped by the next scan line.</summary>
    public double LineOverlap { get; init; }

    /// <summary>Energy delivered per unit area over all passes, J/mm² (= W ÷ (mm/s × mm)).</summary>
    public double ArealEnergyJPerMm2 { get; init; }

    public static Physics From(MopaSettings s, SourceProfile source, LensProfile lens)
    {
        double p = source.RatedPowerW * s.PowerPercent / 100.0;
        double eMJ = s.FrequencyKHz > 0 ? p / s.FrequencyKHz : double.NaN;
        double peakKW = s.PulseWidthNs > 0 ? eMJ / s.PulseWidthNs * 1000.0 : double.NaN;

        double spotMm = lens.SpotDiameterUm / 1000.0;
        double spotAreaCm2 = Math.PI * Math.Pow(spotMm / 20.0, 2); // radius in cm = d(mm)/2/10
        double fluence = spotAreaCm2 > 0 ? eMJ / 1000.0 / spotAreaCm2 : double.NaN;

        double pitchUm = s.FrequencyKHz > 0 ? s.SpeedMmPerS / s.FrequencyKHz : double.NaN;
        double linePitchMm = s.LinePitchMm;

        return new Physics
        {
            AveragePowerW = p,
            PulseEnergyMJ = eMJ,
            PeakPowerKW = peakKW,
            FluenceJPerCm2 = fluence,
            PulsePitchUm = pitchUm,
            PulseOverlap = lens.SpotDiameterUm > 0 ? 1.0 - pitchUm / lens.SpotDiameterUm : double.NaN,
            LineOverlap = spotMm > 0 ? 1.0 - linePitchMm / spotMm : double.NaN,
            ArealEnergyJPerMm2 = s.SpeedMmPerS > 0 && linePitchMm > 0
                ? Math.Max(1, s.Passes) * p / (s.SpeedMmPerS * linePitchMm)
                : double.NaN,
        };
    }

    /// <summary>
    /// The coordinates the planner measures distance in. Logs for the quantities that
    /// span decades, so a step from 0.1 to 0.2 J/mm² counts the same as 1 to 2.
    /// Heat per area, energy per pulse, pulse duration and spot-to-spot overlap are
    /// the four things that change the oxide; the rest follow from them.
    /// </summary>
    public double[] PlanningVector(double pulseWidthNs) =>
    [
        Math.Log10(Math.Max(ArealEnergyJPerMm2, 1e-6)),
        Math.Log10(Math.Max(PulseEnergyMJ, 1e-6)),
        Math.Log10(Math.Max(pulseWidthNs, 1e-3)),
        Math.Clamp(PulseOverlap, -1.0, 1.0),
    ];
}
