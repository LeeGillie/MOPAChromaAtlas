using MOPAChroma.Color;
using MOPAChroma.Laser;

namespace MOPAChroma.Atlas;

public enum RepeatStatus
{
    /// <summary>Only ever engraved once. A single square can be luck.</summary>
    NotRepeated,
    /// <summary>Engraved again on another card and came out the same colour.</summary>
    Verified,
    /// <summary>Engraved again and came out different: the process window is too narrow, or something unrecorded is moving.</summary>
    RepeatDiffers,
}

public sealed record RepeatCheck(RepeatStatus Status, int Repeats, double? BestDeltaE00);

/// <summary>
/// The field guide's repeatability gate, applied automatically. A colour counts as
/// verified only when the same settings, engraved on a different card, measured the same
/// way, came out within <see cref="SameColour"/> ΔE00. No extra step is needed: a
/// micro-grid with odd counts repeats its seed in its centre cell.
///
/// "Same settings" allows for MakeIt's typing precision: power within 1 percentage point,
/// speed and frequency within 2 %, everything else identical (Z within 0.05 mm).
/// </summary>
public static class Repeatability
{
    public const double SameColour = 3.0;

    public static RepeatCheck Check(Sample s, IEnumerable<Sample> all)
    {
        if (!s.HasColor || s.GridId is null) return new RepeatCheck(RepeatStatus.NotRepeated, 0, null);

        var repeats = all.Where(o =>
                o.Id != s.Id && o.HasColor && o.GridId is not null && o.GridId != s.GridId
                && o.MaterialId == s.MaterialId && o.SourceId == s.SourceId && o.LensId == s.LensId
                && o.Method == s.Method
                && SameSettings(o.Settings, s.Settings))
            .ToList();
        if (repeats.Count == 0) return new RepeatCheck(RepeatStatus.NotRepeated, 0, null);

        double best = repeats.Min(o => DeltaE.Cie2000(o.Color!.Value, s.Color!.Value));
        return new RepeatCheck(best <= SameColour ? RepeatStatus.Verified : RepeatStatus.RepeatDiffers, repeats.Count, best);
    }

    public static bool SameSettings(MopaSettings a, MopaSettings b) =>
        Math.Abs(a.PowerPercent - b.PowerPercent) <= 1.0
        && Rel(a.SpeedMmPerS, b.SpeedMmPerS) <= 0.02
        && Rel(a.FrequencyKHz, b.FrequencyKHz) <= 0.02
        && Math.Abs(a.PulseWidthNs - b.PulseWidthNs) < 1e-6
        && Math.Abs(a.LineDensityPerCm - b.LineDensityPerCm) < 1e-6
        && a.Passes == b.Passes
        && Math.Abs(a.FocusOffsetMm - b.FocusOffsetMm) <= 0.05
        && Math.Abs(a.HatchAngleDeg - b.HatchAngleDeg) < 1e-6;

    private static double Rel(double x, double y) =>
        Math.Abs(x - y) / Math.Max(1e-9, Math.Max(Math.Abs(x), Math.Abs(y)));

    public static string Label(RepeatStatus s) => s switch
    {
        RepeatStatus.Verified => "verified",
        RepeatStatus.RepeatDiffers => "repeat differs",
        _ => "not yet repeated",
    };
}
