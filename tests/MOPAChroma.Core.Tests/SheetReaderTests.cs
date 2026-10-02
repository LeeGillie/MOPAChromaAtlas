using System.Globalization;
using MOPAChroma.Atlas;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Scanning;
using Xunit.Abstractions;

namespace MOPAChroma.Tests;

/// <summary>A whole sheet with several numbered grids, "scanned" in one picture.</summary>
public class SheetReaderTests(ITestOutputHelper output)
{
    private const double PxPerMm = 8;
    private const int W = 1600, H = 1200; // machine X 0-200, Y 0-150

    private static TestGrid Grid(int cols, int rows, double cell, double gap, int number, int seed) => new()
    {
        Title = $"g{number}", MaterialId = "ss", SourceId = SourceProfile.Presets[0].Id, LensId = LensProfile.Presets[0].Id,
        Number = number,
        Layout = new GridLayout { Columns = cols, Rows = rows, CellSizeMm = cell, GapMm = gap },
        Cells = Enumerable.Range(0, rows * cols).Select(i => new GridCell(i / cols, i % cols, GridLayout.Label(i / cols, i % cols),
            new MopaSettings { PowerPercent = 40 + (i * 7 + seed * 13) % 55, SpeedMmPerS = 500, LineDensityPerCm = 300,
                               FrequencyKHz = 100 + seed * 20, PulseWidthNs = 60, Passes = 1 })).ToList(),
    };

    private static void Engrave(byte[] px, string gcode)
    {
        double cx = 0, cy = 0, s = 0;
        foreach (var raw in gcode.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('G')) continue;
            double nx = Word(line, 'X') ?? cx, ny = Word(line, 'Y') ?? cy;
            if (Word(line, 'S') is { } sv) s = sv;
            if (line.StartsWith("G1") && s >= 350)
            {
                var col = ((byte)(40 + s / 6), (byte)(200 - s / 8), (byte)(90 + s / 9));
                int steps = (int)Math.Ceiling(Math.Max(Math.Abs(nx - cx), Math.Abs(ny - cy)) * PxPerMm * 2) + 1;
                for (int k = 0; k <= steps; k++)
                {
                    double t = k / (double)steps;
                    int x = (int)((cx + (nx - cx) * t) * PxPerMm), y = (int)((cy + (ny - cy) * t) * PxPerMm);
                    if (x >= 0 && y >= 0 && x < W && y < H) { int i = (y * W + x) * 3; px[i] = col.Item1; px[i + 1] = col.Item2; px[i + 2] = col.Item3; }
                }
            }
            cx = nx; cy = ny;
        }
    }

    private static double? Word(string line, char letter)
    {
        int i = line.IndexOf(letter, 1);
        if (i < 0) return null;
        int j = i + 1;
        while (j < line.Length && (char.IsDigit(line[j]) || line[j] is '.' or '-')) j++;
        return j > i + 1 ? double.Parse(line[(i + 1)..j], CultureInfo.InvariantCulture) : null;
    }

    private static RgbImage Sheet(IEnumerable<(TestGrid g, double x, double y)> placed)
    {
        var px = new byte[W * H * 3];
        for (int i = 0; i < px.Length; i += 3) { px[i] = 16; px[i + 1] = 16; px[i + 2] = 20; }
        foreach (var (g, x, y) in placed) Engrave(px, GcodeExport.ToGcode(g, x, y, cornerMarks: true));
        return new RgbImage(W, H, px);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_numbered_grid_on_a_sheet_is_found_and_matched(bool turned)
    {
        var a = Grid(8, 3, 8, 2, 5, 1);
        var b = Grid(8, 3, 8, 2, 6, 2);
        var c = Grid(8, 3, 8, 2, 7, 3);
        // Column D of grid 7 is too weak to mark: a gap inside one grid, not a second grid.
        c = c with { Cells = c.Cells.Select(x => x.Column == 3 ? x with { Settings = x.Settings with { PowerPercent = 10 } } : x).ToList() };
        var micro = Grid(9, 5, 4, 1, 12, 4);
        var notInAtlas = Grid(8, 3, 8, 2, 99, 5);
        var doc = new AtlasDocument();
        foreach (var g in new[] { a, b, c, micro }) doc.Grids.Add(g);

        var img = Sheet([(a, 50, 25), (b, 150, 25), (c, 50, 75), (micro, 150, 80), (notInAtlas, 60, 125)]);
        if (turned) img = img.Oriented(2, false);
        if (Environment.GetEnvironmentVariable("MOPACHROMA_DUMP") is { Length: > 0 } dir)
            img.SavePng(Path.Combine(dir, $"sheet{(turned ? "-turned" : "")}.png"));

        var found = SheetReader.FindAll(img, doc, output.WriteLine);
        Assert.Equal(new[] { 5, 6, 7, 12 }, found.Select(f => f.Grid.Number).Order().ToArray());
        foreach (var f in found)
        {
            Assert.Equal(turned ? 180 : 0, f.Detection.TurnDegrees);
            foreach (var cell in f.Grid.Cells)
                Assert.Equal(cell.Settings.PowerPercent < 35 ? Outcome.NoMark : Outcome.Good, f.Reading.At(cell.Row, cell.Column).Suggested);
        }
    }

    [Fact]
    public void Grids_without_numbers_are_left_alone()
    {
        var a = Grid(8, 3, 8, 2, 0, 1);
        var doc = new AtlasDocument();
        doc.Grids.Add(a);
        Assert.Empty(SheetReader.FindAll(Sheet([(a, 100, 75)]), doc));
    }

    /// <summary>
    /// Lee's titanium sheet of 2026-10-01 (samples/scans, not in the repository; skipped when
    /// absent): grids 1 and 2 (10 × 10, 5 mm cells, 1.5 mm gaps) beside a MakeIt colour test
    /// with smaller squares and older test cells. Many cells are blank or lighter than the
    /// metal, grid 1 sits on a smudge, and the two grids are not quite on one lattice. Read as
    /// none before 2026-10-01 evening.
    /// </summary>
    [Fact]
    public void Reads_both_grids_on_the_titanium_sheet()
    {
        string? path = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && path is null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "samples", "scans", "titanium-sheet-2026-10-01.png")))
                path = Path.Combine(dir.FullName, "samples", "scans", "titanium-sheet-2026-10-01.png");
        if (path is null) { output.WriteLine("titanium-sheet-2026-10-01.png not present: skipped"); return; }

        var layout = new GridLayout { Rows = 10, Columns = 10, CellSizeMm = 5, GapMm = 1.5 };
        var cells = Enumerable.Range(0, 100).Select(i => new GridCell(i / 10, i % 10, $"{(char)('A' + i % 10)}{i / 10 + 1}",
            new MopaSettings { PowerPercent = 30 + i % 10 * 5, SpeedMmPerS = 800, LineDensityPerCm = 1000, FrequencyKHz = 200, PulseWidthNs = 6 })).ToList();
        var doc = new AtlasDocument();
        foreach (int n in new[] { 1, 2 })
            doc.Grids.Add(new TestGrid { Number = n, Title = $"Ti grid {n}", MaterialId = "ti", SourceId = "s", LensId = "l", Layout = layout, Cells = cells });

        var found = SheetReader.FindAll(RgbImage.Load(path), doc, s => output.WriteLine(s));
        Assert.Equal([1, 2], found.Select(f => f.Grid.Number).Order());
        // Grid 1's first cell (top left) sits at about (67, 581) in this scan, grid 2's at (677, 573).
        var at = found.ToDictionary(f => f.Grid.Number, f => f.Detection.Grid.CentreAt(0, 0));
        Assert.True((at[1] - new PointD(67, 581)).Length < 6, $"grid 1 at {at[1]}");
        Assert.True((at[2] - new PointD(677, 573)).Length < 6, $"grid 2 at {at[2]}");
    }
}

/// <summary>Grids with fewer cells than the coupon holds are shaped to their cells.</summary>
public class GridShapeTests
{
    private static readonly SourceProfile Src = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];
    private static readonly GridLayout Coupon = new() { Columns = 13, Rows = 7, CellSizeMm = 4, GapMm = 1 };

    private static IEnumerable<(MopaSettings, string)> Picks(int n) => Enumerable.Range(0, n).Select(i =>
        (new MopaSettings { PowerPercent = 40 + i, SpeedMmPerS = 1000, LineDensityPerCm = 500, FrequencyKHz = 200, PulseWidthNs = 6, Passes = 1 }, ""));

    [Theory]
    [InlineData(20, 10, 2)]   // two full rows, not 13 + 7
    [InlineData(13, 13, 1)]
    [InlineData(14, 7, 2)]
    [InlineData(25, 13, 2)]   // two rows of 13, the second one short
    [InlineData(1, 2, 1)]
    [InlineData(91, 13, 7)]   // a full coupon is unchanged
    public void A_grid_is_no_bigger_than_its_cells(int n, int cols, int rows)
    {
        var g = TestGrid.Arrange("m", "Micro", Coupon, Picks(n), Src, Lens, "m");
        Assert.Equal(n, g.Cells.Count);
        Assert.Equal((cols, rows), (g.Layout.Columns, g.Layout.Rows));
        Assert.All(g.Cells, c => Assert.True(c.Row < rows && c.Column < cols));
    }

    [Fact]
    public void An_old_full_size_grid_is_trimmed_to_its_cells()
    {
        var full = new TestGrid
        {
            Title = "old", MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id, Layout = Coupon,
            Cells = Picks(20).Select((p, i) => new GridCell(i / 13, i % 13, GridLayout.Label(i / 13, i % 13), p.Item1)).ToList(),
        };
        var t = full.Trimmed();
        Assert.Equal((13, 2), (t.Layout.Columns, t.Layout.Rows));
        Assert.Same(t, t.Trimmed());
    }

    [Fact]
    public void A_micro_grid_with_a_part_filled_last_row_is_found_and_read()
    {
        // 23 cells: 2 rows of 12, the last one short of a cell.
        var g = TestGrid.Arrange("m", "Micro", Coupon with { CellSizeMm = 6, GapMm = 1.5 }, Picks(23), Src, Lens, "m") with { Number = 17 };
        Assert.Equal((12, 2), (g.Layout.Columns, g.Layout.Rows));
        var doc = new AtlasDocument();
        doc.Grids.Add(g);
        var px = new byte[1600 * 1200 * 3];
        for (int i = 0; i < px.Length; i += 3) { px[i] = 16; px[i + 1] = 16; px[i + 2] = 20; }
        var job = GcodeExport.ToGcode(g, 100, 75, cornerMarks: true);
        double cx = 0, cy = 0, s = 0;
        foreach (var raw in job.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('G')) continue;
            double? W(char l) { int i = line.IndexOf(l, 1); if (i < 0) return null; int j = i + 1; while (j < line.Length && (char.IsDigit(line[j]) || line[j] is '.' or '-')) j++; return j > i + 1 ? double.Parse(line[(i + 1)..j], System.Globalization.CultureInfo.InvariantCulture) : null; }
            double nx = W('X') ?? cx, ny = W('Y') ?? cy;
            if (W('S') is { } sv) s = sv;
            if (line.StartsWith("G1") && s > 0)
            {
                int steps = (int)Math.Ceiling(Math.Max(Math.Abs(nx - cx), Math.Abs(ny - cy)) * 16) + 1;
                for (int k = 0; k <= steps; k++)
                {
                    double t = k / (double)steps;
                    int x = (int)((cx + (nx - cx) * t) * 8), y = (int)((cy + (ny - cy) * t) * 8);
                    if (x >= 0 && y >= 0 && x < 1600 && y < 1200) { int i = (y * 1600 + x) * 3; px[i] = (byte)(40 + s / 6); px[i + 1] = (byte)(200 - s / 8); px[i + 2] = (byte)(90 + s / 9); }
                }
            }
            cx = nx; cy = ny;
        }
        var found = SheetReader.FindAll(new RgbImage(1600, 1200, px), doc);
        var f = Assert.Single(found);
        Assert.Equal(17, f.Grid.Number);
        Assert.All(g.Cells, c => Assert.Equal(Outcome.Good, f.Reading.At(c.Row, c.Column).Suggested));
    }
}
