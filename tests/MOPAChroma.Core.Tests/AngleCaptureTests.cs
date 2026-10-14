using MOPAChroma.AngleCapture;
using MOPAChroma.Atlas;
using MOPAChroma.Capture.Package;
using MOPAChroma.Capture.Rig;
using MOPAChroma.Capture.Run;
using MOPAChroma.Capture.Simulation;
using MOPAChroma.Grids;
using LumosUltra.Jobs;
namespace MOPAChroma.Tests;

/// <summary>The capture box's packages, imported into an atlas (simulated rig end to end).</summary>
public sealed class AngleCaptureTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("atlas-capture-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static (AtlasDocument Doc, TestGrid Grid) AtlasWithGrid()
    {
        var doc = AtlasDocument.CreateDefault();
        var src = doc.Sources[0]; var lens = doc.Lenses[0]; var mat = doc.Materials[0];
        var layout = new GridLayout { Columns = 4, Rows = 2 };
        var cells = new List<GridCell>();
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 4; c++)
                cells.Add(new GridCell(r, c, GridLayout.Label(r, c), new MopaSettings
                {
                    PowerPercent = 30 + 8 * (r * 4 + c), SpeedMmPerS = 500, LineDensityPerCm = 500, FrequencyKHz = 200, PulseWidthNs = 6,
                }));
        var grid = doc.AddGrid(new TestGrid { Title = "Capture test", MaterialId = mat.Id, SourceId = src.Id, LensId = lens.Id, Layout = layout, Cells = cells });
        return (doc, grid);
    }

    private async Task<CapturePackage> SimulatedRun(AtlasDocument doc, TestGrid grid, CapturePlan plan)
    {
        var sim = new SimulatedRig();
        var scene = new SimScene
        {
            Width = 960, Height = 600, PixelsPerMm = 3.4,
            Cells = CaptureCards.SimulatedCells(grid, doc.Sources[0], doc.Lenses[0]),
        };
        await using var rig = new RigClient(sim);
        var runner = new CaptureRunner(rig, new SimulatedCamera(sim, scene), scene.Rig);
        var result = await runner.RunAsync(plan, new GridRef { AtlasGridId = grid.Id, Label = grid.Title }, _dir, simulated: true, null, default);
        Assert.True(result.Complete, result.Error);
        return CapturePackage.Load(result.Folder);
    }

    private static CapturePlan Small => new()
    {
        TurnsDeg = [0, 90], TiltsDeg = [-20, 0, 10, 30], Leds = [1, 5], LevelsPercent = [100, 25, 6.25],
        GrabsPerShot = 1, DiscardAfterChange = 0, LedSettle = TimeSpan.Zero, TiltSettle = TimeSpan.Zero,
    };

    [Fact]
    public async Task A_simulated_package_imports_one_angle_table_per_cell()
    {
        var (doc, grid) = AtlasWithGrid();
        var pkg = await SimulatedRun(doc, grid, Small);
        var report = CaptureImport.Import(doc, pkg, CellMap.Centred(grid), allowSimulated: true);

        Assert.Equal(grid.Cells.Count, report.Sets.Count);
        Assert.All(report.Sets, s =>
        {
            Assert.True(s.Simulated);
            Assert.Equal(grid.Id, s.GridId);
            // 2 turns x 4 tilts x 2 LEDs, less any overexposed at every level.
            Assert.InRange(s.Points.Count, 12, 16);
            Assert.All(s.Points, p =>
            {
                Assert.InRange(p.ViewDeg, 0, 65);
                Assert.InRange(p.LightDeg, 0, 90);
                Assert.InRange(p.HalfDeg, 0, 90);
                Assert.Equal(3, p.Rgb.Length);
            });
        });
        Assert.Equal(report.Sets.Count, doc.AngleSets.Count);

        // Importing the same session again replaces it rather than adding to it.
        CaptureImport.Import(doc, pkg, CellMap.Centred(grid), allowSimulated: true);
        Assert.Equal(report.Sets.Count, doc.AngleSets.Count);
    }

    [Fact]
    public async Task Angles_are_worked_out_per_cell_and_the_reflection_peaks_where_optics_says()
    {
        var (doc, grid) = AtlasWithGrid();
        var plan = Small with { TurnsDeg = [0], TiltsDeg = [-22.5, -15, -7.5, 0, 10], Leds = [5], LevelsPercent = [100, 25, 6.25, 1.5] };
        var pkg = await SimulatedRun(doc, grid, plan);
        var set = CaptureImport.Import(doc, pkg, CellMap.Centred(grid), allowSimulated: true).Sets.First();
        // LED 6 sits 30 deg off the camera axis, on the +Y side: its mirror reflection reaches the camera
        // when the card faces half-way towards it, at a tilt of -15 deg (a positive tilt lifts +Y, facing
        // the card away). There the half-way angle is smallest and the cell brightest.
        var nearest = set.Points.MinBy(p => p.HalfDeg)!;
        Assert.InRange(nearest.TiltDeg, -23, -7);
        var brightest = set.Points.MaxBy(p => p.Rgb.Sum())!;
        Assert.InRange(brightest.TiltDeg, -23, -7);
        Assert.True(nearest.HalfDeg < 8, $"half-way angle {nearest.HalfDeg:0.0}");
    }

    [Fact]
    public async Task Simulated_packages_never_reach_a_real_atlas()
    {
        var (doc, grid) = AtlasWithGrid();
        var pkg = await SimulatedRun(doc, grid, Small with { TurnsDeg = [0], TiltsDeg = [0], Leds = [5], LevelsPercent = [100] });
        Assert.Throws<InvalidOperationException>(() => CaptureImport.Import(doc, pkg, CellMap.Centred(grid), allowSimulated: false));
        Assert.Empty(doc.AngleSets);
    }

    [Fact]
    public void Schema_2_is_written_only_when_there_is_angle_data()
    {
        var (doc, grid) = AtlasWithGrid();
        var path = Path.Combine(_dir, "atlas.json");
        doc.Save(path);
        Assert.Equal(1, AtlasDocument.Load(path).Schema);
        doc.AngleSets.Add(new AngleSet { MaterialId = grid.MaterialId, SourceId = grid.SourceId, LensId = grid.LensId, Settings = grid.Cells[0].Settings, CellLabel = "A1" });
        doc.Save(path);
        var back = AtlasDocument.Load(path);
        Assert.Equal(2, back.Schema);
        Assert.Single(back.AngleSets);
    }
}
