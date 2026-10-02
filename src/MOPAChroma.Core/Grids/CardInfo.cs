namespace MOPAChroma.Grids;

/// <summary>
/// What has to be written down about a physical test card for its colours to be
/// reproducible later. MakeIt presets do not keep all of this (Z, lot, cleaning), so the
/// atlas does. Focus / Z itself lives in each cell's settings, because it is part of the
/// recipe, not a note.
/// </summary>
public sealed record CardInfo
{
    /// <summary>Engraved or written on the card; ties the card, the scan and the atlas together.</summary>
    public string CardId { get; init; } = "";

    /// <summary>Stock lot or shipment. A new pack of blanks can shift colours.</summary>
    public string Lot { get; init; } = "";

    /// <summary>How the blank was cleaned before engraving, e.g. "IPA wipe".</summary>
    public string Cleaning { get; init; } = "";

    /// <summary>MakeIt version used to set up and send the card.</summary>
    public string MakeItVersion { get; init; } = "";

    public string Notes { get; init; } = "";

    /// <summary>
    /// Next card ID for a material: its ID plus a running number, e.g. "ss304-mirror-G04".
    /// </summary>
    public static string NextId(string materialId, IEnumerable<string> existing)
    {
        var prefix = materialId + "-G";
        int max = 0;
        foreach (var id in existing)
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(id.AsSpan(prefix.Length), out int n))
                max = Math.Max(max, n);
        return $"{prefix}{max + 1:00}";
    }
}

/// <summary>
/// A card the atlas has proposed (for example a micro-grid around a promising cell) and
/// that is waiting to be engraved and scanned. The scan importer offers it, so its
/// settings never have to be retyped.
/// </summary>
public sealed record PlannedCard
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string MaterialId { get; init; }
    public required string SourceId { get; init; }
    public required string LensId { get; init; }
    public required ColorTestDefinition Definition { get; init; }
    public string CardId { get; init; } = "";
    public string Purpose { get; init; } = "";
    /// <summary>The measured cell this card was planned around, if any.</summary>
    public Guid? SeedSampleId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Set once the engraved card has been imported.</summary>
    public Guid? CompletedGridId { get; init; }

    public string Display => $"{CardId} · {Purpose}";
}
