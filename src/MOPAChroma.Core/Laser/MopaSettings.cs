namespace MOPAChroma.Laser;

/// <summary>
/// One set of machine settings, in the units the WeCreat MakeIt UI shows, so a cell
/// can be typed straight into MakeIt without conversion.
///
/// MakeIt units, confirmed from its staged G-code (see the Lumos Ultra LightBurn
/// config project): speed is mm/s in the UI (G1F carries mm/min), power is percent
/// (S 0..1000 in G-code), line density is lines per CENTIMETRE, frequency is kHz
/// (M38F), pulse width is ns (M39P).
/// </summary>
public sealed record MopaSettings
{
    public double PowerPercent { get; init; }
    public double SpeedMmPerS { get; init; }
    public double LineDensityPerCm { get; init; }
    public double FrequencyKHz { get; init; }
    public double PulseWidthNs { get; init; }
    public int Passes { get; init; } = 1;
    public double HatchAngleDeg { get; init; }
    public double FocusOffsetMm { get; init; }

    /// <summary>Distance between scan lines in mm.</summary>
    public double LinePitchMm => LineDensityPerCm > 0 ? 10.0 / LineDensityPerCm : double.NaN;

    public string ToShortString() =>
        $"P{PowerPercent:0.#}% v{SpeedMmPerS:0.#} d{LineDensityPerCm:0.#} f{FrequencyKHz:0.#} τ{PulseWidthNs:0.#}" +
        (Passes > 1 ? $" ×{Passes}" : "");
}
