using System.Text.Json;
using System.Text.Json.Serialization;
using MOPAChroma.Grids;
using LumosUltra.Jobs;
namespace MOPAChroma.Atlas;

/// <summary>
/// Everything one atlas knows, in one human-readable JSON file. Chosen over a
/// database for version 0.x because it diffs cleanly in git and can be shared as a
/// single attachment. The plan is SQLite once the sample count makes JSON slow.
/// </summary>
public sealed class AtlasDocument
{
    /// <summary>
    /// Schema 2 added <see cref="AngleSets"/>. A file is written as schema 1 while it has none,
    /// so older versions of Atlas still open it; once it has some, older versions refuse it
    /// (rather than open it and drop the angle data on their next save).
    /// </summary>
    public const int CurrentSchema = 2;

    public int Schema { get; set; } = CurrentSchema;
    public List<Material> Materials { get; set; } = [];
    public List<SourceProfile> Sources { get; set; } = [];
    public List<LensProfile> Lenses { get; set; } = [];
    public List<TestGrid> Grids { get; set; } = [];
    public List<Sample> Samples { get; set; } = [];
    public List<PlannedCard> PlannedCards { get; set; } = [];
    /// <summary>Colours over angles from the capture box, per cell (schema 2).</summary>
    public List<AngleSet> AngleSets { get; set; } = [];

    public static AtlasDocument CreateDefault() => new()
    {
        Sources = SourceProfile.Presets.ToList(),
        Lenses = LensProfile.Presets.ToList(),
        Materials =
        [
            new Material { Id = "ss304-brushed", Name = "Stainless steel", Family = MaterialFamily.StainlessSteel, Grade = "304", Finish = "brushed" },
            new Material { Id = "ss-mirror", Name = "Stainless steel", Family = MaterialFamily.StainlessSteel, Finish = "mirror" },
            new Material { Id = "ti-gr2", Name = "Titanium", Family = MaterialFamily.Titanium, Grade = "Grade 2" },
        ],
    };

    /// <summary>Samples for one material on one source and lens - the only set it is valid to reason over together.</summary>
    public IReadOnlyList<Sample> SamplesFor(string materialId, string sourceId, string lensId) =>
        Samples.Where(s => s.MaterialId == materialId && s.SourceId == sourceId && s.LensId == lensId).ToList();

    /// <summary>
    /// Records measured results for an existing grid. A cell's pending placeholder is
    /// replaced; a cell that already has a measured result keeps it and gains the new one
    /// as a re-measure (samples are never edited). Returns how many placeholders were filled.
    /// </summary>
    public int RecordResults(IEnumerable<Sample> results)
    {
        int filled = 0;
        foreach (var r in results)
        {
            int i = Samples.FindIndex(s => s.GridId == r.GridId && s.GridId is not null
                                           && s.CellLabel == r.CellLabel && s.Outcome == Outcome.Pending);
            if (i >= 0) { Samples[i] = r; filled++; }
            else Samples.Add(r);
        }
        return filled;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AtlasDocument Load(string path)
    {
        using var f = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<AtlasDocument>(f, Options)
                  ?? throw new InvalidDataException($"{path} is not an atlas file.");
        if (doc.Schema > CurrentSchema)
            throw new InvalidDataException($"{path} was written by a newer MOPAChroma Atlas (schema {doc.Schema}).");
        doc.NumberGrids();
        return doc;
    }

    /// <summary>Gives a number to every grid that has none (atlases from before grid numbers), oldest first.</summary>
    public void NumberGrids()
    {
        int next = Grids.Count == 0 ? 1 : Grids.Max(g => g.Number) + 1;
        var unnumbered = Grids.Select((g, i) => (g, i)).Where(x => x.g.Number <= 0).OrderBy(x => x.g.CreatedUtc).ToList();
        foreach (var (g, i) in unnumbered) Grids[i] = g with { Number = next++ };
    }

    /// <summary>The number the next grid added will get.</summary>
    public int NextGridNumber() => Grids.Count == 0 ? 1 : Grids.Max(g => g.Number) + 1;

    /// <summary>Adds a grid, giving it the next grid number (never reused) if it has none.</summary>
    public TestGrid AddGrid(TestGrid grid)
    {
        if (grid.Number <= 0) grid = grid with { Number = NextGridNumber() };
        Grids.Add(grid);
        return grid;
    }

    /// <summary>Write via a temporary file so a crash mid-save never destroys the atlas.</summary>
    public void Save(string path)
    {
        Schema = AngleSets.Count > 0 ? 2 : 1;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
