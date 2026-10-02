using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Planning;

namespace MOPAChroma.Tests;

public class CardTests
{
    private static readonly SourceProfile Source = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];

    [Fact]
    public void Material_test_array_sweeps_power_and_speed_with_frequency_fixed()
    {
        var def = ColorTestDefinition.MaterialTestArray() with { FrequencyKHz = 250, FocusZMm = 0.5 };
        Assert.Null(def.Problem());
        var first = def.SettingsAt(0, 0);
        var last = def.SettingsAt(def.Rows - 1, def.Columns - 1);
        Assert.Equal(250, first.FrequencyKHz);
        Assert.Equal(250, last.FrequencyKHz);
        Assert.NotEqual(first.PowerPercent, last.PowerPercent);
        Assert.NotEqual(first.SpeedMmPerS, last.SpeedMmPerS);
        Assert.Equal(0.5, last.FocusOffsetMm); // Z is part of every cell's recipe
    }

    [Fact]
    public void Same_axis_twice_is_rejected()
    {
        var def = new ColorTestDefinition { RowSweep = new(CardAxis.Power, 10, 20, 5, true) };
        Assert.NotNull(def.Problem());
    }

    [Fact]
    public void Micro_grid_follows_the_field_guide_and_repeats_the_seed_in_its_centre()
    {
        var parent = ColorTestDefinition.UltraColourTest();
        var seed = parent.SettingsAt(3, 12); // 283.3 kHz, 71.6 %
        var micro = MicroGrid.Around(seed, parent, Source, rows: 5, columns: 9);

        Assert.Null(micro.Problem());
        Assert.Equal(CardAxis.Frequency, micro.RowSweep.Axis);   // parent's axes kept
        Assert.Equal(CardAxis.Power, micro.ColumnSweep.Axis);
        Assert.Equal(10, micro.ColumnSweep.Max - micro.ColumnSweep.Min, 1); // ±5 points
        // ±20 %, each side floored to whole kHz so it can be typed: 283.3 → 227-339.
        Assert.InRange(micro.RowSweep.Max - micro.RowSweep.Min, 0.4 * seed.FrequencyKHz - 2, 0.4 * seed.FrequencyKHz);

        var centre = micro.SettingsAt(2, 4);
        Assert.True(Repeatability.SameSettings(seed, centre), $"{seed.ToShortString()} vs {centre.ToShortString()}");
        Assert.Equal(seed.SpeedMmPerS, centre.SpeedMmPerS);
        Assert.Equal(seed.PulseWidthNs, centre.PulseWidthNs);
    }

    [Fact]
    public void Micro_grid_on_speed_uses_about_a_quarter_either_side()
    {
        var seed = new MopaSettings { PowerPercent = 60, SpeedMmPerS = 1000, FrequencyKHz = 200, PulseWidthNs = 200, LineDensityPerCm = 400 };
        var micro = MicroGrid.Around(seed, null, Source); // no parent: speed rows x power columns
        Assert.Equal(CardAxis.Speed, micro.RowSweep.Axis);
        Assert.InRange(micro.RowSweep.Min, 770, 800);
        Assert.InRange(micro.RowSweep.Max, 1200, 1230);
        Assert.Equal(200, micro.FrequencyKHz);
    }

    [Fact]
    public void Power_near_the_limit_is_pulled_in_symmetrically()
    {
        var seed = new MopaSettings { PowerPercent = 98, SpeedMmPerS = 800, FrequencyKHz = 200, PulseWidthNs = 6 };
        var micro = MicroGrid.Around(seed, ColorTestDefinition.UltraColourTest(), Source);
        Assert.True(micro.ColumnSweep.Max <= 100);
        Assert.Equal(98, (micro.ColumnSweep.Min + micro.ColumnSweep.Max) / 2, 1);
    }

    private static Sample S(Guid grid, MopaSettings s, Lab lab) => new()
    {
        MaterialId = "m", SourceId = "s", LensId = "l", GridId = grid,
        Settings = s, Outcome = Outcome.Good, Color = lab, Method = MeasureMethod.Scanner,
    };

    [Fact]
    public void A_colour_is_verified_only_by_a_matching_repeat_on_another_card()
    {
        var set = new MopaSettings { PowerPercent = 71.6, SpeedMmPerS = 800, FrequencyKHz = 283.3, PulseWidthNs = 6, LineDensityPerCm = 5000 };
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var original = S(g1, set, new Lab(38, -7, -8));
        var neighbour = S(g1, set with { PowerPercent = 74.2 }, new Lab(38, -7, -8));

        Assert.Equal(RepeatStatus.NotRepeated, Repeatability.Check(original, [original, neighbour]).Status);

        var goodRepeat = S(g2, set with { PowerPercent = 71.5 }, new Lab(39, -6.5, -8.5)); // typed 71.5
        Assert.Equal(RepeatStatus.Verified, Repeatability.Check(original, [original, goodRepeat]).Status);

        var badRepeat = S(g2, set, new Lab(30, 5, -15));
        Assert.Equal(RepeatStatus.RepeatDiffers, Repeatability.Check(original, [original, badRepeat]).Status);

        var otherMethod = goodRepeat with { Id = Guid.NewGuid(), Method = MeasureMethod.Instrument };
        Assert.Equal(RepeatStatus.NotRepeated, Repeatability.Check(original, [original, otherMethod]).Status);
    }

    [Fact]
    public void Colour_finder_prefers_a_verified_colour_that_is_nearly_as_close()
    {
        var target = new Lab(40, -7, -8);
        var a = new MopaSettings { PowerPercent = 70, SpeedMmPerS = 800, FrequencyKHz = 280, PulseWidthNs = 6, LineDensityPerCm = 5000 };
        var b = a with { PowerPercent = 80 };
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var unverifiedExact = S(g1, b, new Lab(40, -7, -8));           // ΔE 0, never repeated
        var verified = S(g1, a, new Lab(40.5, -7, -8));                // ΔE ~0.5, repeated below
        var repeat = S(g2, a, new Lab(40.6, -7.1, -8));

        var found = ColorFinder.Find([unverifiedExact, verified, repeat], target);
        Assert.Equal(RepeatStatus.Verified, found[0].Repeat.Status);
        Assert.Contains(found, m => m.Sample.Id == unverifiedExact.Id && m.Repeat.Status == RepeatStatus.NotRepeated);
    }

    [Fact]
    public void Card_ids_count_up_per_material()
    {
        Assert.Equal("ss-mirror-G01", CardInfo.NextId("ss-mirror", []));
        Assert.Equal("ss-mirror-G04", CardInfo.NextId("ss-mirror", ["ss-mirror-G01", "ss-mirror-G03", "ti-G09", "odd"]));
    }

    [Fact]
    public void Planned_cards_and_card_info_survive_a_save()
    {
        var doc = AtlasDocument.CreateDefault();
        var def = ColorTestDefinition.MaterialTestArray() with { FocusZMm = 1.5 };
        doc.PlannedCards.Add(new PlannedCard { MaterialId = "m", SourceId = "s", LensId = "l", Definition = def, CardId = "m-G02", Purpose = "Micro-grid" });
        doc.Grids.Add(def.ToGrid("t", "m", Source, Lens, card: new CardInfo { CardId = "m-G01", Lot = "2026-09", Cleaning = "IPA", MakeItVersion = "3.0.6" }));

        var path = Path.Combine(Path.GetTempPath(), $"atlas-{Guid.NewGuid():N}.json");
        try
        {
            doc.Save(path);
            var back = AtlasDocument.Load(path);
            Assert.Equal(def, back.PlannedCards[0].Definition);
            Assert.Equal("IPA", back.Grids[0].Card!.Cleaning);
            Assert.Equal(CardAxis.Speed, back.Grids[0].Definition!.RowSweep.Axis);
            Assert.Equal(1.5, back.Grids[0].Cells[0].Settings.FocusOffsetMm);
        }
        finally { File.Delete(path); }
    }
}
