namespace MOPAChroma.Atlas;

/// <summary>Broad material families. Colour behaviour differs completely between them.</summary>
public enum MaterialFamily
{
    StainlessSteel,
    Titanium,
    Aluminum,
    AnodizedAluminum,
    Brass,
    Copper,
    BlackStainless,
    Other,
}

/// <summary>
/// The shape of the blank. A big sheet draws heat away from the spot faster than a small
/// card, and a thick coin faster still, so the same settings colour them differently:
/// keep each form a separate material.
/// </summary>
public enum MaterialForm
{
    Unspecified,
    Sheet,
    Card,
    Coin,
    Tag,
    Other,
}

/// <summary>
/// A specific material the atlas is built for. Grade and finish are part of the
/// identity, not notes: 304 and 316 stainless, or brushed and mirror finishes, give
/// different colours at the same settings, so their results must never be pooled.
/// </summary>
public sealed record Material
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public MaterialFamily Family { get; init; }

    /// <summary>Alloy or grade, e.g. "304", "Grade 2".</summary>
    public string Grade { get; init; } = "";

    /// <summary>Surface finish, e.g. "brushed #4", "mirror", "bead blasted".</summary>
    public string Finish { get; init; } = "";

    public double ThicknessMm { get; init; }
    /// <summary>Sheet, card, coin… (see <see cref="MaterialForm"/>).</summary>
    public MaterialForm Form { get; init; }
    public string Supplier { get; init; } = "";
    /// <summary>Supplier's product code, so a reorder gets the same stock.</summary>
    public string Sku { get; init; } = "";
    public string Notes { get; init; } = "";

    /// <summary>Coupon stock size for this material, if known. Drives how many cells the
    /// test-grid planner fits onto a coupon; null until the user sets it once.</summary>
    public double? CouponWidthMm { get; init; }
    public double? CouponHeightMm { get; init; }

    /// <summary>Last cell size and gap used for this material's test grids, if any;
    /// null until the user sets them once.</summary>
    public double? CellSizeMm { get; init; }
    public double? GapMm { get; init; }

    /// <summary>Corner radius of this material's coupon stock, mm, for the exported
    /// reference frame. Defaults to 3mm (R3) - the standard corner radius for 86x54mm
    /// metal card blanks (ISO/IEC 7810 ID-1 range is 2.88-3.48mm) - until the user
    /// measures their actual stock and sets it.</summary>
    public double CornerRadiusMm { get; init; } = 3.0;

    /// <summary>The settings the planner may use for this material; null means <see cref="PlanningRanges.Default"/>.</summary>
    public PlanningRanges? Ranges { get; init; }

    public string DisplayName =>
        string.Join(" · ", new[] { Name, Grade, Finish, ThicknessMm > 0 ? $"{ThicknessMm:0.##} mm" : "",
                                   Form == MaterialForm.Unspecified ? "" : Form.ToString().ToLowerInvariant() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    public static string FamilyLabel(MaterialFamily f) => f switch
    {
        MaterialFamily.StainlessSteel => "Stainless steel",
        MaterialFamily.AnodizedAluminum => "Anodized aluminum",
        MaterialFamily.BlackStainless => "Black stainless",
        _ => f.ToString(),
    };
}
