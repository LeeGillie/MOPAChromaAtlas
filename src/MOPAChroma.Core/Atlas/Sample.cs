using MOPAChroma.Color;
using LumosUltra.Jobs;
namespace MOPAChroma.Atlas;

/// <summary>What happened in a cell, apart from its colour.</summary>
public enum Outcome
{
    /// <summary>Planned, not yet engraved or not yet measured.</summary>
    Pending,
    /// <summary>A usable, even colour.</summary>
    Good,
    /// <summary>No visible change. Marks the low-energy edge of the map.</summary>
    NoMark,
    /// <summary>Uneven, streaky or blotchy. Colour recorded but unreliable.</summary>
    Uneven,
    /// <summary>Ablated, melted, discoloured grey/black by overheating. Marks the high-energy edge.</summary>
    Damaged,
}

/// <summary>How the colour was obtained. Ordered roughly by trustworthiness.</summary>
public enum MeasureMethod
{
    None,
    /// <summary>Picked by eye or from an uncalibrated photo.</summary>
    Estimated,
    /// <summary>
    /// Flatbed scan in the standard orientation, not colour-calibrated. Consistent between
    /// scans on the same scanner; not an absolute colour. (Enums are stored by name in
    /// the atlas file, so inserting a member here does not change existing files.)
    /// </summary>
    Scanner,
    /// <summary>Photo or scan with a colour reference in frame, corrected against it.</summary>
    CalibratedImage,
    /// <summary>Colorimeter or spectrophotometer reading.</summary>
    Instrument,
}

/// <summary>
/// One engraved cell: the settings that were used and what came out. Samples are
/// never edited after measurement; a re-measure is a new sample. This keeps every
/// colour in the atlas traceable to a physical coupon.
/// </summary>
public sealed record Sample
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string MaterialId { get; init; }
    public required string SourceId { get; init; }
    public required string LensId { get; init; }
    public Guid? GridId { get; init; }
    public string CellLabel { get; init; } = "";
    public required MopaSettings Settings { get; init; }

    public Outcome Outcome { get; init; } = Outcome.Pending;
    public Lab? Color { get; init; }
    public MeasureMethod Method { get; init; } = MeasureMethod.None;

    /// <summary>
    /// Viewing angle matters: oxide colours shift with angle. Recorded so readings
    /// taken differently are not silently compared.
    /// </summary>
    public string ViewingCondition { get; init; } = "";

    public DateTimeOffset? MeasuredUtc { get; init; }
    public string Notes { get; init; } = "";

    public bool HasColor => Color is not null && Outcome is Outcome.Good or Outcome.Uneven;
}
