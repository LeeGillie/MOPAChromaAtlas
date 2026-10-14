using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Grids;
using LumosUltra.Jobs;
using MOPAChroma.Planning;

namespace MOPAChromaAtlas;

/// <summary>
/// A throwaway atlas for screenshots and trying the workflow (--demo). Its "results"
/// come from a made-up material model, NOT from engraving, and it is never written
/// over the user's real atlas.
/// </summary>
internal static class DemoAtlas
{
    public static AtlasDocument Create()
    {
        var doc = AtlasDocument.CreateDefault();
        var mat = doc.Materials[0];
        var src = doc.Sources[0];
        var lens = doc.Lenses[0];
        // Planned exactly as the main window plans: inside the material's planning ranges.
        var planner = new GridPlanner(ParameterSpace.For(src, mat.Ranges), src, lens);
        var layout = new GridLayout { Columns = 6, Rows = 4 };

        var seed = TestGrid.Arrange($"{mat.Name} {mat.Grade} — grid 1", "Seed", layout,
            planner.Seed(layout.Capacity).Select(p => (p.Settings, p.Reason)), src, lens, mat.Id);
        seed = doc.AddGrid(seed);
        foreach (var c in seed.Cells)
            doc.Samples.Add(Simulate(mat.Id, src, lens, seed.Id, c));

        var refine = TestGrid.Arrange($"{mat.Name} {mat.Grade} — grid 2", "Refine #1", layout,
            planner.Refine(layout.Capacity, doc.Samples).Select(p => (p.Settings, p.Reason)), src, lens, mat.Id);
        refine = doc.AddGrid(refine);
        foreach (var c in refine.Cells)
            doc.Samples.Add(new Sample
            {
                MaterialId = mat.Id, SourceId = src.Id, LensId = lens.Id,
                GridId = refine.Id, CellLabel = c.Label, Settings = c.Settings,
            });
        return doc;
    }

    /// <summary>
    /// Invented response: no mark below 1 J/mm², damage above 20, and between them a
    /// thin-film-like cycle straw → bronze → purple → blue → teal as energy rises.
    /// </summary>
    private static Sample Simulate(string materialId, SourceProfile src, LensProfile lens, Guid gridId, GridCell c)
    {
        double e = Physics.From(c.Settings, src, lens).ArealEnergyJPerMm2;
        const double lo = 1, hi = 20;
        var outcome = e < lo ? Outcome.NoMark : e > hi ? Outcome.Damaged : Outcome.Good;
        Lab? lab = null;
        if (outcome == Outcome.Good)
        {
            double t = (Math.Log10(e) - Math.Log10(lo)) / (Math.Log10(hi) - Math.Log10(lo));
            double h = (80 + 300 * t) * Math.PI / 180;
            lab = new Lab(72 - 30 * t, 38 * Math.Cos(h), 38 * Math.Sin(h));
        }
        return new Sample
        {
            MaterialId = materialId, SourceId = src.Id, LensId = lens.Id,
            GridId = gridId, CellLabel = c.Label, Settings = c.Settings,
            Outcome = outcome, Color = lab,
            Method = lab is null ? MeasureMethod.None : MeasureMethod.Estimated,
            Notes = "SIMULATED (demo)",
            MeasuredUtc = DateTimeOffset.UtcNow,
        };
    }
}
