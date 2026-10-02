using System.Xml.Linq;
using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Planning;
using Xunit.Abstractions;

namespace MOPAChroma.Tests;

public class PlanningTests(ITestOutputHelper output)
{
    private static readonly SourceProfile Source = SourceProfile.Presets[0]; // 100 W
    private static readonly LensProfile Lens = LensProfile.Presets[0] with { SpotDiameterUm = 40 };

    [Fact]
    public void Physics_is_computed_in_the_stated_units()
    {
        var s = new MopaSettings
        {
            PowerPercent = 50, SpeedMmPerS = 1000, LineDensityPerCm = 500,
            FrequencyKHz = 100, PulseWidthNs = 200,
        };
        var p = Physics.From(s, Source, Lens);

        Assert.Equal(50, p.AveragePowerW, 9);
        Assert.Equal(0.5, p.PulseEnergyMJ, 9);      // 50 W / 100 kHz
        Assert.Equal(2.5, p.PeakPowerKW, 9);        // 0.5 mJ / 200 ns
        Assert.Equal(10, p.PulsePitchUm, 9);        // 1000 mm/s / 100 kHz
        Assert.Equal(0.75, p.PulseOverlap, 9);      // 1 - 10/40
        Assert.Equal(0.5, p.LineOverlap, 9);        // 1 - 0.02/0.04
        Assert.Equal(2.5, p.ArealEnergyJPerMm2, 9); // 50 / (1000 × 0.02)
    }

    [Fact]
    public void Physically_equivalent_settings_share_a_planning_position()
    {
        var a = new MopaSettings { PowerPercent = 30, SpeedMmPerS = 500, LineDensityPerCm = 400, FrequencyKHz = 200, PulseWidthNs = 100 };
        var b = a with { PowerPercent = 60, SpeedMmPerS = 1000, FrequencyKHz = 400 }; // everything doubled

        var va = Physics.From(a, Source, Lens).PlanningVector(a.PulseWidthNs);
        var vb = Physics.From(b, Source, Lens).PlanningVector(b.PulseWidthNs);
        for (int i = 0; i < va.Length; i++) Assert.Equal(va[i], vb[i], 9);
    }

    [Fact]
    public void Seed_proposes_distinct_valid_settings_inside_the_space()
    {
        var space = ParameterSpace.DefaultFor(Source);
        var planner = new GridPlanner(space, Source, Lens, poolSize: 1500);
        var picks = planner.Seed(24);

        Assert.Equal(24, picks.Count);
        Assert.Equal(24, picks.Select(p => p.Settings).Distinct().Count());
        foreach (var p in picks)
        {
            Assert.True(space.IsValid(p.Settings, p.Physics));
            Assert.InRange(p.Settings.PowerPercent, space.PowerPercent.Min, space.PowerPercent.Max);
            Assert.Contains(p.Settings.PulseWidthNs, space.PulseWidthsNs);
        }
    }

    [Fact]
    public void Seed_spreads_energy_across_orders_of_magnitude()
    {
        var planner = new GridPlanner(ParameterSpace.DefaultFor(Source), Source, Lens, poolSize: 1500);
        var e = planner.Seed(24).Select(p => p.Physics.ArealEnergyJPerMm2).ToList();
        Assert.True(e.Max() / e.Min() > 30, $"energy span only {e.Max() / e.Min():0.0}x");
    }

    /// <summary>
    /// A made-up material: no mark below 0.3 J/mm², damage above 8 J/mm², and hue that
    /// rotates with log energy between. Refinement should waste fewer cells in the
    /// dead zones than the blind seed did.
    /// </summary>
    [Fact]
    public void Refinement_spends_fewer_cells_in_dead_zones_than_the_seed()
    {
        var planner = new GridPlanner(ParameterSpace.DefaultFor(Source), Source, Lens, poolSize: 1500);
        var seed = planner.Seed(24);
        var samples = seed.Select(p => Measure(p.Settings)).ToList();

        var refined = planner.Refine(24, samples);
        Assert.Equal(24, refined.Count);

        double Dead(IEnumerable<Proposal> ps) =>
            ps.Count(p => p.Physics.ArealEnergyJPerMm2 is < 0.3 or > 8) / (double)ps.Count();

        output.WriteLine($"dead-zone cells: seed {Dead(seed):P0}, refined {Dead(refined):P0}");
        foreach (var r in refined) output.WriteLine($"  {r.Settings.ToShortString(),-40} E={r.Physics.ArealEnergyJPerMm2,8:0.###}  {r.Reason}");

        // At the time of writing: seed 50 %, refined 21 %. Fail if that edge erodes badly.
        Assert.True(Dead(refined) <= 0.6 * Dead(seed),
            $"refined dead fraction {Dead(refined):P0} vs seed {Dead(seed):P0}");
        Assert.DoesNotContain(refined, r => seed.Any(s => s.Settings == r.Settings));
    }

    private static Sample Measure(MopaSettings s)
    {
        double e = Physics.From(s, Source, Lens).ArealEnergyJPerMm2;
        var outcome = e < 0.3 ? Outcome.NoMark : e > 8 ? Outcome.Damaged : Outcome.Good;
        Lab? lab = null;
        if (outcome == Outcome.Good)
        {
            double t = (Math.Log10(e) - Math.Log10(0.3)) / (Math.Log10(8) - Math.Log10(0.3));
            double h = t * 2 * Math.PI;
            lab = new Lab(60 - 20 * t, 40 * Math.Cos(h), 40 * Math.Sin(h));
        }
        return new Sample
        {
            MaterialId = "test", SourceId = Source.Id, LensId = Lens.Id,
            Settings = s, Outcome = outcome, Color = lab,
            Method = lab is null ? MeasureMethod.None : MeasureMethod.Instrument,
        };
    }

    [Fact]
    public void Grid_svg_is_valid_svg_with_one_rect_per_cell()
    {
        var planner = new GridPlanner(ParameterSpace.DefaultFor(Source), Source, Lens, poolSize: 800);
        var layout = new GridLayout { Columns = 5, Rows = 3 };
        var grid = TestGrid.Arrange("t", "Seed", layout, planner.Seed(15).Select(p => p.Settings), Source, Lens, "m");

        var doc = XDocument.Parse(GridExport.ToSvg(grid));
        XNamespace svg = "http://www.w3.org/2000/svg";
        Assert.Equal(svg + "svg", doc.Root!.Name);
        Assert.Equal(15, doc.Descendants(svg + "rect").Count());
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.Namespace != svg);
        Assert.Equal("A1", grid.Cells[0].Label);
        Assert.Equal("E3", grid.Cells[^1].Label);

        var csv = GridExport.ToCsv(grid, Source, Lens).TrimEnd().Split('\n');
        Assert.Equal(16, csv.Length);
    }

    [Fact]
    public void Atlas_round_trips_through_json()
    {
        var doc = AtlasDocument.CreateDefault();
        doc.Samples.Add(Measure(new MopaSettings { PowerPercent = 40, SpeedMmPerS = 800, LineDensityPerCm = 500, FrequencyKHz = 150, PulseWidthNs = 200 }));
        var path = Path.Combine(Path.GetTempPath(), $"atlas-{Guid.NewGuid():N}.json");
        try
        {
            doc.Save(path);
            var back = AtlasDocument.Load(path);
            Assert.Equal(doc.Samples[0], back.Samples[0]);
            Assert.Equal(doc.Materials.Count, back.Materials.Count);
            Assert.Equal(doc.Sources[0].PulseWidthsNs, back.Sources[0].PulseWidthsNs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Colour_finder_ranks_by_delta_e()
    {
        var target = new Lab(50, 0, -40);
        var s = new[]
        {
            Make(new Lab(50, 0, 40)),
            Make(new Lab(51, 1, -38)),
            Make(new Lab(50, 10, -20)),
        };
        var found = ColorFinder.Find(s, target);
        Assert.Equal(new Lab(51, 1, -38), found[0].Sample.Color);

        static Sample Make(Lab c) => new()
        {
            MaterialId = "m", SourceId = "s", LensId = "l", Outcome = Outcome.Good, Color = c,
            Settings = new MopaSettings { PowerPercent = 1 },
        };
    }
}
