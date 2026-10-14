using MOPAChroma.Atlas;
using LumosUltra.Jobs;
namespace MOPAChroma.Planning;

/// <summary>
/// Hard limits on heat per area (J/mm²: power ÷ (speed × line spacing) × passes), the quantity
/// that decides whether a cell colours the surface or cuts into it. Nothing Atlas plans or
/// sends may exceed the ceiling. Colour is a thin oxide; it never needs the heat that removes
/// metal, and a planner that "learns where damage starts" must not do it by burning through
/// the blank and into the machine (2026-10-01: five cells of one grid cut into or through a
/// stainless sheet and marked the plate beneath).
///
/// The ceiling is the lowest of:
/// - <see cref="AbsoluteMaxJPerMm2"/>, always, even before any result exists;
/// - <see cref="AboveHottestGood"/> × the hottest cell that came out Good, once one exists, so
///   colour seeking reaches a little beyond what worked and no further;
/// - the coolest Damaged cell, once one exists (but never below the hottest Good cell): past
///   that point there is nothing to learn that is worth the risk.
/// </summary>
public static class HeatLimits
{
    /// <summary>
    /// Never planned or sent above this, whatever the results say. Far below what cuts steel
    /// (hundreds of J/mm² and up), and above the heat colour tests normally use.
    /// </summary>
    public const double AbsoluteMaxJPerMm2 = 40;

    /// <summary>How far beyond the hottest Good cell colour seeking may go.</summary>
    public const double AboveHottestGood = 1.5;

    public static double ArealEnergy(MopaSettings s, SourceProfile source, LensProfile lens) =>
        Physics.From(s, source, lens).ArealEnergyJPerMm2;

    /// <summary>The ceiling for one material, source and lens, from its results so far.</summary>
    public static double Ceiling(IEnumerable<Sample> samples, SourceProfile source, LensProfile lens)
    {
        double ceiling = AbsoluteMaxJPerMm2;
        var measured = samples.Where(s => s.Outcome != Outcome.Pending).ToList();
        var good = measured.Where(s => s.Outcome == Outcome.Good).Select(s => ArealEnergy(s.Settings, source, lens)).Where(double.IsFinite).ToList();
        var damaged = measured.Where(s => s.Outcome == Outcome.Damaged).Select(s => ArealEnergy(s.Settings, source, lens)).Where(double.IsFinite).ToList();
        double hottestGood = good.Count > 0 ? good.Max() : 0;
        if (good.Count > 0) ceiling = Math.Min(ceiling, AboveHottestGood * hottestGood);
        if (damaged.Count > 0) ceiling = Math.Min(ceiling, Math.Max(damaged.Min(), hottestGood));
        return ceiling;
    }

    /// <summary>Cells of a grid hotter than <paramref name="ceiling"/>, with their heat, hottest first.</summary>
    public static IReadOnlyList<(string Label, double JPerMm2)> TooHot(Grids.TestGrid grid, double ceiling, SourceProfile source, LensProfile lens) =>
        grid.Cells.Select(c => (c.Label, J: ArealEnergy(c.Settings, source, lens)))
                  .Where(x => !double.IsFinite(x.J) || x.J > ceiling + 1e-9)
                  .OrderByDescending(x => x.J).ToList();
}
