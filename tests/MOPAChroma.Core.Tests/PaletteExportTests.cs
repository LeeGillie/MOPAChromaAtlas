using System.Xml.Linq;
using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Export;
using LumosUltra.Jobs;
namespace MOPAChroma.Tests;

public class PaletteExportTests
{
    private static readonly Material Steel = new() { Id = "ss", Name = "Stainless steel", Grade = "304", Finish = "mirror" };
    private static readonly SourceProfile Source = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];

    private static MopaSettings Recipe(double power, double pw = 200) => new()
    {
        PowerPercent = power, SpeedMmPerS = 800, FrequencyKHz = 250, PulseWidthNs = pw, LineDensityPerCm = 5000, Passes = 1,
    };

    private static Sample S(MopaSettings m, Outcome o, Lab? c, Guid grid, MeasureMethod method = MeasureMethod.Estimated, int minutes = 0) => new()
    {
        MaterialId = "ss", SourceId = Source.Id, LensId = Lens.Id, GridId = grid, CellLabel = "A1",
        Settings = m, Outcome = o, Color = c, Method = method, MeasuredUtc = DateTimeOffset.UnixEpoch.AddMinutes(minutes),
    };

    private static List<Sample> Atlas()
    {
        Guid g1 = Guid.NewGuid(), g2 = Guid.NewGuid();
        return
        [
            // Blue, engraved twice on different cards and read the same: verified.
            S(Recipe(60), Outcome.Good, new Lab(35, 5, -40), g1),
            S(Recipe(60), Outcome.Good, new Lab(36, 4, -39), g2),
            // Gold, once only.
            S(Recipe(30), Outcome.Good, new Lab(70, 5, 45), g1),
            // Not colours: left out.
            S(Recipe(10), Outcome.NoMark, null, g1),
            S(Recipe(90), Outcome.Damaged, new Lab(30, 0, 0), g1),
            S(Recipe(45), Outcome.Uneven, new Lab(50, 20, 10), g1),
        ];
    }

    [Fact]
    public void Palette_holds_only_good_colours_one_per_recipe_verified_first()
    {
        var p = Palette.Build(Atlas(), "ss", Source.Id, Lens.Id);
        Assert.Equal(2, p.Count);
        Assert.True(p[0].Verified);
        Assert.Contains("blue", p[0].Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, p[0].Readings);
        Assert.False(p[1].Verified);
        Assert.Contains("gold", p[1].Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_most_recent_picture_or_scan_is_the_colour_used()
    {
        // An earlier scan (washed out on metal) is replaced by a later photo, and the other way round.
        var all = Atlas();
        var g = Guid.NewGuid();
        all.Add(S(Recipe(30), Outcome.Good, new Lab(60, 20, 40), g, MeasureMethod.Scanner, minutes: -100));
        var gold = Palette.Build(all, "ss", Source.Id, Lens.Id).Single(e => e.Settings.PowerPercent == 30);
        Assert.Equal(MeasureMethod.Estimated, gold.Method);   // the photo at minute 0 is newer
        all.Add(S(Recipe(30), Outcome.Good, new Lab(62, 18, 41), g, MeasureMethod.Scanner, minutes: 100));
        gold = Palette.Build(all, "ss", Source.Id, Lens.Id).Single(e => e.Settings.PowerPercent == 30);
        Assert.Equal(MeasureMethod.Scanner, gold.Method);
        Assert.Equal(62, gold.Color.L);
        // An instrument reading wins whatever its age.
        all.Add(S(Recipe(30), Outcome.Good, new Lab(55, 10, 30), g, MeasureMethod.Instrument, minutes: -500));
        Assert.Equal(MeasureMethod.Instrument, Palette.Build(all, "ss", Source.Id, Lens.Id).Single(e => e.Settings.PowerPercent == 30).Method);
    }

    [Fact]
    public void Ids_do_not_change_when_a_recipe_is_measured_again()
    {
        var a = Palette.Build(Atlas(), "ss", Source.Id, Lens.Id);
        var more = Atlas();
        more.Add(S(Recipe(30), Outcome.Good, new Lab(66, 8, 44), Guid.NewGuid(), MeasureMethod.Scanner));
        var b = Palette.Build(more, "ss", Source.Id, Lens.Id);
        Assert.Equal(a.Select(e => e.Id).Order(), b.Select(e => e.Id).Order());
    }

    [Fact]
    public void LightBurn_library_has_the_right_shape_and_units()
    {
        var p = Palette.Build(Atlas(), "ss", Source.Id, Lens.Id);
        var r = LightBurnLibrary.Write(p, Steel, Source, Lens, verifiedOnly: false, DateTimeOffset.UnixEpoch);
        var x = XDocument.Parse(r.Clb);
        Assert.Equal("LightBurnLibrary", x.Root!.Name.LocalName);
        var mat = Assert.Single(x.Root.Elements("Material"));
        Assert.Equal(Steel.DisplayName, (string?)mat.Attribute("name"));
        var entries = mat.Elements("Entry").ToList();
        Assert.Equal(2, entries.Count);
        var blue = entries.First();
        Assert.Equal("-1.0000", (string?)blue.Attribute("Thickness"));
        Assert.Equal(LightBurnLibrary.Desc(p[0]), (string?)blue.Attribute("Desc"));
        var cs = blue.Element("CutSetting")!;
        Assert.Equal("Scan", (string?)cs.Attribute("type"));
        string V(string n) => (string)cs.Element(n)!.Attribute("Value")!;
        Assert.Equal("60", V("maxPower"));
        Assert.Equal("800", V("speed"));            // mm/s
        Assert.Equal("250000", V("frequency"));     // Hz
        Assert.Equal("0.002", V("interval"));       // mm: 10 / 5000 per cm
        Assert.Equal("1", V("numPasses"));
        Assert.Equal($"{Steel.DisplayName}/-1.0000/{LightBurnLibrary.Desc(p[0])}", V("LinkPath"));
        Assert.Contains("\"entries\"", r.SidecarJson);
    }

    [Fact]
    public void Verified_only_leaves_out_single_readings()
    {
        var p = Palette.Build(Atlas(), "ss", Source.Id, Lens.Id);
        var r = LightBurnLibrary.Write(p, Steel, Source, Lens, verifiedOnly: true, DateTimeOffset.UnixEpoch);
        Assert.Equal(1, r.Entries);
    }

    [Fact]
    public void Pulse_width_is_snapped_to_one_the_source_accepts()
    {
        var src = Source with { PulseWidthsNs = [2, 4, 6, 9, 13, 20, 30, 45] };
        Assert.Equal(30, Palette.SnapPulseWidth(33, src));
        Assert.Equal(9, Palette.SnapPulseWidth(11, src)); // tie between 9 and 13 goes to the shorter
        var all = new List<Sample> { S(Recipe(50, pw: 33), Outcome.Good, new Lab(40, 0, -30), Guid.NewGuid()) };
        var r = LightBurnLibrary.Write(Palette.Build(all, "ss", src.Id, Lens.Id), Steel, src, Lens, false, DateTimeOffset.UnixEpoch);
        Assert.Equal(1, r.PulseWidthsSnapped);
        Assert.Contains("<QPulseWidth Value=\"30\" />", r.Clb);
    }

    [Theory]
    [InlineData(35, 5, -40, "Deep blue")]
    [InlineData(70, 5, 45, "Gold")]
    [InlineData(80, 1, 2, "Silver")]
    [InlineData(45, 30, -30, "Purple")]
    [InlineData(55, -30, 10, "Green")]
    public void Colours_get_plain_names(double l, double a, double b, string name) =>
        Assert.Equal(name, ColorNames.Name(new Lab(l, a, b)));

    [Fact]
    public void MakeIt_palette_is_a_plain_project_with_one_square_and_recipe_per_colour()
    {
        var p = Palette.Build(Atlas(), "ss", Source.Id, Lens.Id);
        var r = MakeItPalette.Write(p, Steel, Source, Lens, verifiedOnly: false, DateTimeOffset.UnixEpoch);
        Assert.Equal(2, r.Entries);
        Assert.StartsWith("{", r.Project); // plain JSON, as MakeIt 3.0.5 wrote; never MakeIt's encrypted WWS2

        var doc = System.Text.Json.Nodes.JsonNode.Parse(r.Project)!;
        Assert.Equal("3.0.5", (string?)doc["version"]);
        var canvas = doc["canvasList"]![0]!;
        Assert.Equal((string?)doc["currentCanvasId"], (string?)canvas["id"]);
        Assert.Equal((string?)canvas["id"], (string?)canvas["workModeData"]!["canvasID"]);
        var all = canvas["objects"]!.AsArray();
        var objects = all.Where(o => (string?)o!["type"] == "rect").ToList();
        Assert.Equal(2, objects.Count);
        // Each square has a "row.column" label, at 0 % power so it never marks.
        var labels = all.Where(o => (string?)o!["type"] == "i-text").ToList();
        Assert.Equal(["1.1", "1.2"], labels.Select(l => (string?)l!["text"]));
        var procs = doc["processList"]!.AsObject();
        Assert.All(labels, l => Assert.Equal(0, (int)procs[(string)l!["id"]!]!["engrave"]!["power"]!));

        var inOrder = Palette.ByColour(p);
        for (int i = 0; i < 2; i++)
        {
            var o = objects[i]!;
            var e = inOrder[i];
            Assert.Equal("rect", (string?)o["type"]);
            Assert.Equal(e.Hex, (string?)o["fill"]);
            Assert.Equal(MakeItPalette.SquareMm, (double)o["width"]!);
            var pr = procs[(string)o["id"]!]!;
            Assert.Equal("fillEngrave", (string?)pr["processMode"]);
            Assert.Equal(e.Settings.PowerPercent, (double)pr["fillEngrave"]!["power"]!);
            Assert.Equal(e.Settings.SpeedMmPerS, (double)pr["fillEngrave"]!["speed"]!);
            Assert.Equal(1, (int)pr["fillEngrave"]!["repeat"]!);
            Assert.Equal(250, (double)pr["frequency"]!);
            Assert.Equal(Palette.SnapPulseWidth(200, Source), (double)pr["pulseWidth"]!);
            Assert.Equal(5000, (int)pr["lineDesity"]!);
            Assert.Equal(12700, (int)pr["dpi"]!);
        }
        // Squares do not overlap.
        Assert.True((double)objects[1]!["left"]! >= (double)objects[0]!["left"]! + MakeItPalette.SquareMm);

        // A layer per colour, listing its square.
        var rows = doc["layerDataList"]![0]!["data"]!.AsArray();
        Assert.All(all, o => Assert.Contains(rows, row => (string?)row!["id"] == (string?)o!["id"]));
        Assert.All(all, o => Assert.NotNull(procs[(string)o!["id"]!]));
        Assert.StartsWith("data:image/png;base64,iVBORw0KGgo", (string?)doc["cover"]);
        Assert.Contains("1.1", r.Legend);
        Assert.Contains("1.2", r.Legend);

        Assert.Equal(1, MakeItPalette.Write(p, Steel, Source, Lens, verifiedOnly: true, DateTimeOffset.UnixEpoch).Entries);
    }

    [Fact]
    public void MakeIt_palette_stays_inside_the_lens_field_and_pages_when_full()
    {
        // 300 different good colours: more than one page holds.
        var g = Guid.NewGuid();
        var samples = Enumerable.Range(0, 300).Select(i => S(Recipe(30 + i % 30 * 2) with { FrequencyKHz = 150 + i / 30 * 15 }, Outcome.Good, new Lab(40 + i % 50, i % 30 - 15, i % 40 - 20), g)).ToList();
        var p = Palette.Build(samples, "ss", Source.Id, Lens.Id);
        Assert.Equal(300, p.Count);
        var r = MakeItPalette.Write(p, Steel, Source, Lens, verifiedOnly: false, DateTimeOffset.UnixEpoch);
        Assert.Equal(2, r.Projects.Count);
        Assert.Contains("Page 2 of 2", r.Legend);

        double field = Lens.FieldSizeMm > 0 ? Lens.FieldSizeMm : 150;
        double lo = MakeItPalette.CanvasCentreMm - field / 2, hi = MakeItPalette.CanvasCentreMm + field / 2;
        int total = 0;
        foreach (var project in r.Projects)
        {
            var objs = System.Text.Json.Nodes.JsonNode.Parse(project)!["canvasList"]![0]!["objects"]!.AsArray();
            var squares = objs.Where(o => (string?)o!["type"] == "rect").ToList();
            total += squares.Count;
            foreach (var o in objs)
            {
                double x = (double)o!["left"]!, y = (double)o["top"]!, w = (double)o["width"]!, h = (double)o["height"]!;
                Assert.InRange(x, lo, hi); Assert.InRange(x + w, lo, hi);
                Assert.InRange(y, lo, hi); Assert.InRange(y + h, lo, hi);
            }
            // No two squares overlap.
            var boxes = squares.Select(o => ((double)o!["left"]!, (double)o["top"]!, (double)o["width"]!)).ToList();
            for (int a = 0; a < boxes.Count; a++)
                for (int b = a + 1; b < boxes.Count; b++)
                    Assert.False(Math.Abs(boxes[a].Item1 - boxes[b].Item1) < boxes[a].Item3 && Math.Abs(boxes[a].Item2 - boxes[b].Item2) < boxes[a].Item3);
        }
        Assert.Equal(300, total);

        // Few colours keep full-size squares.
        Assert.Equal(MakeItPalette.SquareMm, MakeItPalette.LayoutFor(20, 150).Square, 1);
    }

    [Fact]
    public void MakeIt_materials_match_the_palette_squares_and_keep_their_ids()
    {
        var p = Palette.Build(Atlas(), "ss", Source.Id, Lens.Id);
        var r = MakeItMaterials.Write(p, Steel, Source, Lens, verifiedOnly: false, DateTimeOffset.UnixEpoch);
        Assert.Equal(2, r.Entries);
        var mats = System.Text.Json.Nodes.JsonNode.Parse(r.Json)!.AsArray();

        // Same order and labels as the palette project's squares.
        var palette = System.Text.Json.Nodes.JsonNode.Parse(MakeItPalette.Write(p, Steel, Source, Lens, false, DateTimeOffset.UnixEpoch).Project)!;
        var squareLabels = palette["canvasList"]![0]!["objects"]!.AsArray().Where(o => (string?)o!["type"] == "i-text").Select(o => (string)o!["text"]!).ToList();
        var chosen = MakeItPalette.Chosen(p, false);
        for (int i = 0; i < 2; i++)
        {
            var m = mats[i]!;
            Assert.StartsWith($"{Steel.Name} {squareLabels[i]} ", (string)m["title"]!);
            Assert.Equal("lumosUltra", (string?)m["deviceType"]);
            Assert.Equal("custom", (string?)m["type"]);
            var fe = m["mode"]!["fillEngrave"]!;
            Assert.Equal(chosen[i].Settings.PowerPercent, (double)fe["power"]!);
            Assert.Equal(250, (double)fe["frequency"]!);
            Assert.Equal(5000, (int)fe["lineDesity"]!);
            Assert.StartsWith("data:image/png;base64,iVBORw0KGgo", (string?)m["reference"]!["referenceImg"]);
        }
        // Ids are well-formed UUIDs (version 5, RFC 4122 variant), as JavaScript uuid checks expect.
        Assert.All(mats, m => Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", (string)m!["id"]!));
        // A later export gives the same colours the same ids.
        var again = System.Text.Json.Nodes.JsonNode.Parse(MakeItMaterials.Write(p, Steel, Source, Lens, false, DateTimeOffset.Now).Json)!.AsArray();
        Assert.Equal(mats.Select(m => (string?)m!["id"]), again.Select(m => (string?)m!["id"]));
    }
}
