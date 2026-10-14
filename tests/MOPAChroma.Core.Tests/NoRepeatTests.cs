using MOPAChroma.Atlas;
using MOPAChroma.Color;
using LumosUltra.Jobs;
using MOPAChroma.Planning;

namespace MOPAChroma.Tests;

/// <summary>Nothing already engraved is planned again, apart from the one deliberate repeat in a micro-grid.</summary>
public class NoRepeatTests
{
    private static readonly SourceProfile Src = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];
    private static readonly MopaSettings Seed = new()
    {
        PowerPercent = 60, SpeedMmPerS = 800, FrequencyKHz = 250, PulseWidthNs = Src.PulseWidthsNs[^1], LineDensityPerCm = 500, Passes = 1,
    };

    [Fact]
    public void Micro_grid_repeats_only_its_seed_and_nothing_engraved_before()
    {
        // Two neighbours of the seed were engraved on an earlier card.
        var tested = new List<MopaSettings> { Seed, Seed with { PowerPercent = 61 }, Seed with { SpeedMmPerS = 720 } };
        var cells = MicroGrid.AtlasCells(Seed, Src, tested, capacity: 40);
        Assert.Single(cells, c => Repeatability.SameSettings(c.Settings, Seed));
        Assert.Contains("Repeat", cells[0].Reason);
        foreach (var c in cells.Skip(1))
            Assert.DoesNotContain(tested, t => Repeatability.SameSettings(t, c.Settings));
        // And no two cells of the new grid are the same.
        for (int i = 0; i < cells.Count; i++)
            for (int j = i + 1; j < cells.Count; j++)
                Assert.False(Repeatability.SameSettings(cells[i].Settings, cells[j].Settings));
        // Everything but power and speed is the seed's.
        Assert.All(cells, c => Assert.Equal((Seed.FrequencyKHz, Seed.PulseWidthNs, Seed.LineDensityPerCm),
                                            (c.Settings.FrequencyKHz, c.Settings.PulseWidthNs, c.Settings.LineDensityPerCm)));
    }

    [Fact]
    public void Micro_grid_fills_a_small_card_with_the_nearest_cells()
    {
        var cells = MicroGrid.AtlasCells(Seed, Src, [], capacity: 9);
        Assert.Equal(9, cells.Count);
        Assert.All(cells, c => Assert.InRange(c.Settings.PowerPercent, Seed.PowerPercent - 3, Seed.PowerPercent + 3));
    }

    [Fact]
    public void Refinement_never_plans_a_setting_already_engraved()
    {
        var planner = new GridPlanner(ParameterSpace.DefaultFor(Src), Src, Lens, poolSize: 1500);
        var seed = planner.Seed(30);
        var rnd = new Random(1);
        var samples = seed.Select(p => new Sample
        {
            MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id, GridId = Guid.NewGuid(), Settings = p.Settings,
            Outcome = rnd.NextDouble() < 0.2 ? Outcome.NoMark : Outcome.Good,
            Color = new Lab(50, rnd.NextDouble() * 40 - 20, rnd.NextDouble() * 40 - 20),
        }).ToList();
        var next = planner.Refine(30, samples);
        Assert.NotEmpty(next);
        foreach (var p in next)
            Assert.DoesNotContain(samples, s => Repeatability.SameSettings(s.Settings, p.Settings));
    }

    [Fact]
    public void Refinement_leans_towards_a_rare_colour()
    {
        var planner = new GridPlanner(ParameterSpace.DefaultFor(Src), Src, Lens, poolSize: 1500);
        var seed = planner.Seed(30).Select(p => p.Settings).ToList();
        // Every cell gold, except one blue.
        int blue = 7;
        var samples = seed.Select((m, i) => new Sample
        {
            MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id, GridId = Guid.NewGuid(), Settings = m, Outcome = Outcome.Good,
            Color = i == blue ? new Lab(40, 5, -40) : new Lab(70, 5 + i % 3, 45),
        }).ToList();
        var blueSettings = seed[blue];

        int NearBlue(GridPlanner p) => p.Refine(12, samples).Count(x => x.Reason.Contains("rare colour"));
        var seeking = new GridPlanner(ParameterSpace.DefaultFor(Src), Src, Lens, poolSize: 1500);
        var plain = new GridPlanner(ParameterSpace.DefaultFor(Src), Src, Lens, poolSize: 1500) { ColourSeeking = 0 };
        Assert.True(NearBlue(seeking) > 0, "colour seeking should spend cells near the only blue");
        Assert.Equal(0, NearBlue(plain));
    }
}
