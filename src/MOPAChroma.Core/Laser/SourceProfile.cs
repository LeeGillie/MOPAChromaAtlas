namespace MOPAChroma.Laser;

/// <summary>How much a stated value can be trusted. Carried on every preset.</summary>
public enum Evidence
{
    /// <summary>Typical published figure, not checked on this machine.</summary>
    Nominal,
    /// <summary>Read from the vendor's own documentation or software.</summary>
    VendorStated,
    /// <summary>Measured on the machine by the user.</summary>
    Measured,
}

/// <summary>
/// A MOPA fiber source as the planner needs to know it: its rated power and the
/// settings it will actually accept. Only settings inside these limits are ever
/// proposed.
/// </summary>
public sealed record SourceProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public double RatedPowerW { get; init; }
    public double FrequencyMinKHz { get; init; }
    public double FrequencyMaxKHz { get; init; }

    /// <summary>
    /// Pulse widths the source offers. MOPA seeds switch between a fixed set of
    /// waveforms, so pulse width is a discrete axis, not a continuous one.
    /// </summary>
    public IReadOnlyList<double> PulseWidthsNs { get; init; } = [];

    public double SpeedMaxMmPerS { get; init; }
    public Evidence Evidence { get; init; } = Evidence.Nominal;
    public string Notes { get; init; } = "";

    /// <summary>
    /// Presets for the two Lumos Ultra MOPA options. The ranges are typical JPT M7
    /// figures and are marked Nominal: edit them to what MakeIt actually offers on
    /// your machine before trusting the planner's boundaries.
    /// </summary>
    public static IReadOnlyList<SourceProfile> Presets { get; } =
    [
        new()
        {
            Id = "lumos-ultra-mopa-100w",
            Name = "Lumos Ultra — MOPA 100 W (JPT)",
            RatedPowerW = 100,
            FrequencyMinKHz = 1,
            FrequencyMaxKHz = 4000,
            PulseWidthsNs = [2, 4, 6, 8, 13, 20, 30, 45, 60, 80, 100, 150, 200, 250, 350, 500],
            SpeedMaxMmPerS = 10000,
            Evidence = Evidence.Nominal,
            Notes = "Typical JPT M7 ranges; not yet checked against MakeIt's limits.",
        },
        new()
        {
            Id = "lumos-ultra-mopa-60w",
            Name = "Lumos Ultra — MOPA 60 W (JPT)",
            RatedPowerW = 60,
            FrequencyMinKHz = 1,
            FrequencyMaxKHz = 4000,
            PulseWidthsNs = [2, 4, 6, 8, 13, 20, 30, 45, 60, 80, 100, 150, 200, 250, 350, 500],
            SpeedMaxMmPerS = 10000,
            Evidence = Evidence.Nominal,
            Notes = "Typical JPT M7 ranges; not yet checked against MakeIt's limits.",
        },
    ];
}

/// <summary>
/// A field lens. Spot diameter drives every overlap and fluence figure, so it is the
/// single most important number to measure rather than assume.
/// </summary>
public sealed record LensProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public double FieldSizeMm { get; init; }
    public double SpotDiameterUm { get; init; }
    public Evidence Evidence { get; init; } = Evidence.Nominal;

    public static IReadOnlyList<LensProfile> Presets { get; } =
    [
        new()
        {
            Id = "lumos-ultra-red",
            Name = "Lumos Ultra red lens (MOPA)",
            FieldSizeMm = 150,
            SpotDiameterUm = 40,
            Evidence = Evidence.Nominal,
        },
    ];
}
