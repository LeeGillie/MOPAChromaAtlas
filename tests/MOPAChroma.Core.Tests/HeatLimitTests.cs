using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Planning;

namespace MOPAChroma.Tests;

/// <summary>
/// Nothing is planned or sent hotter than <see cref="HeatLimits"/> allows. Written after the
/// 2026-10-01 grid whose hottest cells (3 passes at very close line spacing) cut through a
/// stainless sheet and marked the plate beneath.
/// </summary>
public class HeatLimitTests
{
    private static readonly SourceProfile Src = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];

    private static double J(MopaSettings s) => HeatLimits.ArealEnergy(s, Src, Lens);

    private static Sample S(MopaSettings m, Outcome o) => new()
    {
        MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id, GridId = Guid.NewGuid(), Settings = m, Outcome = o,
        Color = o == Outcome.Good ? new Lab(60, 5, 30) : null,
    };

    [Fact]
    public void The_space_allows_nothing_above_the_absolute_limit()
    {
        var space = ParameterSpace.DefaultFor(Src);
        var hottest = new MopaSettings { PowerPercent = 90, SpeedMmPerS = 50, LineDensityPerCm = 3000, FrequencyKHz = 500, PulseWidthNs = 200, Passes = 3 };
        Assert.True(J(hottest) > 1000, $"the old range reached {J(hottest):0} J/mm²");
        Assert.False(space.IsValid(hottest, Physics.From(hottest, Src, Lens)));
    }

    [Fact]
    public void A_seed_grid_stays_under_the_absolute_limit()
    {
        var planner = new GridPlanner(ParameterSpace.DefaultFor(Src), Src, Lens, poolSize: 2000);
        var seed = planner.Seed(45);
        Assert.Equal(45, seed.Count);
        Assert.All(seed, p => Assert.True(J(p.Settings) <= HeatLimits.AbsoluteMaxJPerMm2, $"{J(p.Settings):0.0} J/mm²"));
    }

    [Fact]
    public void Refinement_goes_at_most_half_again_past_the_hottest_good_cell()
    {
        var planner = new GridPlanner(ParameterSpace.DefaultFor(Src), Src, Lens, poolSize: 2000);
        // Everything that worked was cool; colour seeking wants more heat.
        var seed = planner.Seed(40).Select(p => p.Settings).Where(s => J(s) <= 4).Take(20).ToList();
        Assert.True(seed.Count >= 8);
        var samples = seed.Select(m => S(m, Outcome.Good)).ToList();
        double hottestGood = seed.Max(J);
        var next = planner.Refine(45, samples);
        Assert.NotEmpty(next);
        Assert.All(next, p => Assert.True(J(p.Settings) <= 1.5 * hottestGood + 1e-9, $"{J(p.Settings):0.0} > 1.5 × {hottestGood:0.0}"));
    }

    [Fact]
    public void After_damage_nothing_hotter_than_the_coolest_damaged_cell_is_planned()
    {
        var planner = new GridPlanner(ParameterSpace.DefaultFor(Src), Src, Lens, poolSize: 2000);
        var seed = planner.Seed(40).Select(p => p.Settings).OrderBy(J).ToList();
        // The cooler half came out Good, the hotter half Damaged.
        var samples = seed.Select((m, i) => S(m, i < seed.Count / 2 ? Outcome.Good : Outcome.Damaged)).ToList();
        double ceiling = HeatLimits.Ceiling(samples, Src, Lens);
        double coolestDamaged = seed.Skip(seed.Count / 2).Min(J), hottestGood = seed.Take(seed.Count / 2).Max(J);
        Assert.Equal(Math.Min(Math.Min(HeatLimits.AbsoluteMaxJPerMm2, 1.5 * hottestGood), Math.Max(coolestDamaged, hottestGood)), ceiling, 6);
        Assert.All(planner.Refine(45, samples), p => Assert.True(J(p.Settings) <= ceiling + 1e-9));
    }

    [Fact]
    public void Many_passes_over_close_lines_count_as_the_heat_they_are()
    {
        // Small pulses, so the old "hotter AND bigger pulses than a damaged cell" test let it
        // through; three passes at 3000 lines/cm still pour heat in.
        var risky = new MopaSettings { PowerPercent = 40, SpeedMmPerS = 300, LineDensityPerCm = 3000, FrequencyKHz = 1000, PulseWidthNs = 13, Passes = 3 };
        Assert.True(J(risky) > HeatLimits.AbsoluteMaxJPerMm2);
        var grid = new TestGrid
        {
            Title = "g", MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id,
            Layout = new GridLayout { Columns = 2, Rows = 1, CellSizeMm = 8, GapMm = 2 },
            Cells = [new GridCell(0, 0, "A1", risky with { Passes = 1, LineDensityPerCm = 300 }), new GridCell(0, 1, "B1", risky)],
        };
        var hot = HeatLimits.TooHot(grid, HeatLimits.AbsoluteMaxJPerMm2, Src, Lens);
        Assert.Equal(["B1"], hot.Select(h => h.Label));
    }

    [Fact]
    public void A_micro_grid_keeps_to_the_limit_it_is_given()
    {
        var seed = new MopaSettings { PowerPercent = 60, SpeedMmPerS = 400, LineDensityPerCm = 500, FrequencyKHz = 250, PulseWidthNs = 200, Passes = 1 };
        double limit = J(seed) * 1.05;
        var cells = MicroGrid.AtlasCells(seed, Src, [], capacity: 45, allowed: s => J(s) <= limit);
        Assert.NotEmpty(cells);
        Assert.All(cells, c => Assert.True(J(c.Settings) <= limit + 1e-9));
    }
}

/// <summary>Per-material planning ranges: everything planned stays inside them.</summary>
public class PlanningRangeTests
{
    private static readonly SourceProfile Src = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];

    [Fact]
    public void A_micro_grid_around_a_good_cell_outside_the_ranges_still_has_cells()
    {
        // A cell from a grid planned before the ranges were set (or changed): 20 % power, 30 kHz.
        var seed = new MopaSettings { PowerPercent = 20, SpeedMmPerS = 1500, LineDensityPerCm = 1000, FrequencyKHz = 30, PulseWidthNs = 6, Passes = 1 };
        var r = PlanningRanges.Default;
        Assert.False(r.Contains(seed));
        Assert.Single(MicroGrid.AtlasCells(seed, Src, [], 24, allowed: r.Contains));   // only the repeat: nothing to send

        var wide = r.Including(seed);
        Assert.True(wide.Contains(seed));
        var cells = MicroGrid.AtlasCells(seed, Src, [], 24, allowed: wide.Contains);
        Assert.True(cells.Count > 10, $"{cells.Count} cells");
        Assert.All(cells, c => Assert.True(c.Settings.PowerPercent >= 20 - 1e-9 && c.Settings.PowerPercent <= 25 + 1e-9));
        Assert.Equal(r.PowerMaxPercent, wide.PowerMaxPercent);   // stretched only towards the cell
        Assert.Equal(r.MaxPasses, r.Including(seed with { Passes = 5 }).MaxPasses);
    }

    [Fact]
    public void A_seed_keeps_to_the_starting_ranges_and_the_heat_limit()
    {
        var r = PlanningRanges.Default; // 30-90 %, 140-400 kHz, 4-7 ns
        var space = ParameterSpace.For(Src, r);
        Assert.Equal([4.0, 6.0], space.PulseWidthsNs);
        var seed = new GridPlanner(space, Src, Lens, poolSize: 2000).Seed(91);
        Assert.Equal(91, seed.Count);
        Assert.All(seed, p =>
        {
            Assert.True(r.Contains(p.Settings), $"{p.Settings}");
            Assert.InRange(p.Settings.PowerPercent, 30, 90);
            Assert.InRange(p.Settings.FrequencyKHz, 140, 400);
            Assert.Contains(p.Settings.PulseWidthNs, new[] { 4.0, 6.0 });
            Assert.True(HeatLimits.ArealEnergy(p.Settings, Src, Lens) <= HeatLimits.AbsoluteMaxJPerMm2);
        });
        // Still spread out: both pulse widths, low and high power, and a range of heat.
        Assert.Equal(2, seed.Select(p => p.Settings.PulseWidthNs).Distinct().Count());
        Assert.True(seed.Min(p => p.Settings.PowerPercent) <= 40 && seed.Max(p => p.Settings.PowerPercent) >= 80);
        var heat = seed.Select(p => HeatLimits.ArealEnergy(p.Settings, Src, Lens)).ToList();
        Assert.True(heat.Max() / heat.Min() > 20, $"{heat.Min():0.00}-{heat.Max():0.0}");
    }

    [Fact]
    public void Refinement_keeps_to_narrowed_ranges()
    {
        var r = PlanningRanges.Default with { PowerMinPercent = 50, PowerMaxPercent = 70, MaxPasses = 1 };
        var planner = new GridPlanner(ParameterSpace.For(Src, r), Src, Lens, poolSize: 2000);
        var seed = planner.Seed(30);
        var samples = seed.Select(p => new MOPAChroma.Atlas.Sample
        {
            MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id, GridId = Guid.NewGuid(), Settings = p.Settings,
            Outcome = MOPAChroma.Atlas.Outcome.Good, Color = new MOPAChroma.Color.Lab(55, 5, 30),
        }).ToList();
        Assert.All(planner.Refine(30, samples), p => Assert.True(r.Contains(p.Settings)));
    }
}
