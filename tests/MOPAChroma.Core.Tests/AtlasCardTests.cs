using System.Globalization;
using MOPAChroma.Atlas;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Scanning;
using Xunit.Abstractions;

namespace MOPAChroma.Tests;

/// <summary>
/// The whole Atlas loop without a laser: plan a grid, generate its job with corner
/// crosses, "engrave" the job into an image straight from its G-code, then find and read
/// the grid in that image. If the crosses GcodeExport writes and the crosses GridDetect
/// looks for ever disagree, these fail.
/// </summary>
public class AtlasCardTests(ITestOutputHelper output)
{
    private const double PxPerMm = 10, ViewX = 50, ViewY = 75; // image shows machine X 50-160, Y 75-135
    private const int W = 1100, H = 600;
    /// <summary>Below this power the simulated material shows nothing (a "no mark" cell).</summary>
    private const int MarksFromS = 350;

    private static TestGrid Grid()
    {
        var layout = new GridLayout { Columns = 8, Rows = 3, CellSizeMm = 8, GapMm = 2 };
        var cells = new List<GridCell>();
        for (int r = 0; r < layout.Rows; r++)
            for (int c = 0; c < layout.Columns; c++)
                cells.Add(new GridCell(r, c, GridLayout.Label(r, c), new MopaSettings
                {
                    // Columns 0 and 1 are too weak to mark: the reader cannot see them.
                    PowerPercent = c < 2 ? 20 + c * 5 : 40 + c * 7,
                    SpeedMmPerS = 800 - r * 100,
                    LineDensityPerCm = 300,
                    FrequencyKHz = 150 + r * 50,
                    PulseWidthNs = 60,
                    Passes = 1,
                }));
        return new TestGrid
        {
            Title = "e2e", MaterialId = "ss-mirror", SourceId = SourceProfile.Presets[0].Id, LensId = LensProfile.Presets[0].Id,
            Layout = layout, Cells = cells,
        };
    }

    /// <summary>Paints every powered G1 move of the job onto dark steel, coloured by its power.</summary>
    private static RgbImage Engrave(string gcode)
    {
        var px = new byte[W * H * 3];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double mx = ViewX + x / PxPerMm, my = ViewY + y / PxPerMm;
                bool onCard = mx > 62 && mx < 148 && my > 78 && my < 132; // an 86 × 54 blank centred on the bed
                Set(px, x, y, onCard ? ((byte)14, (byte)14, (byte)18) : ((byte)230, (byte)230, (byte)225));
            }

        double cx = 0, cy = 0, s = 0;
        foreach (var raw in gcode.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('G')) continue;
            bool g1 = line.StartsWith("G1");
            double nx = Word(line, 'X') ?? cx, ny = Word(line, 'Y') ?? cy;
            if (Word(line, 'S') is { } sv) s = sv;
            if (g1 && s >= MarksFromS)
            {
                var col = ((byte)(40 + s / 6), (byte)(200 - s / 8), (byte)(90 + s / 9));
                int steps = (int)Math.Ceiling(Math.Max(Math.Abs(nx - cx), Math.Abs(ny - cy)) * PxPerMm * 2) + 1;
                for (int k = 0; k <= steps; k++)
                {
                    double t = k / (double)steps;
                    int x = (int)((cx + (nx - cx) * t - ViewX) * PxPerMm), y = (int)((cy + (ny - cy) * t - ViewY) * PxPerMm);
                    if (x >= 0 && y >= 0 && x < W && y < H) Set(px, x, y, col);
                }
            }
            cx = nx; cy = ny;
        }
        return new RgbImage(W, H, px);
    }

    private static void Set(byte[] px, int x, int y, (byte r, byte g, byte b) c)
    {
        int i = (y * W + x) * 3;
        px[i] = c.r; px[i + 1] = c.g; px[i + 2] = c.b;
    }

    private static double? Word(string line, char letter)
    {
        if (line.Length < 2) return null;
        int i = line.IndexOf(letter, 1);
        if (i < 0) return null;
        int j = i + 1;
        while (j < line.Length && (char.IsDigit(line[j]) || line[j] is '.' or '-')) j++;
        return j > i + 1 ? double.Parse(line[(i + 1)..j], CultureInfo.InvariantCulture) : null;
    }

    private static RgbImage Turn180(RgbImage img)
    {
        var px = new byte[img.Pixels.Length];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int s = (y * W + x) * 3, d = ((H - 1 - y) * W + (W - 1 - x)) * 3;
                px[d] = img.Pixels[s]; px[d + 1] = img.Pixels[s + 1]; px[d + 2] = img.Pixels[s + 2];
            }
        return new RgbImage(W, H, px);
    }

    /// <summary>Where cell (column, row) of the grid lands in the image.</summary>
    private static PointD Truth(GridLayout l, int c, int r, bool turned)
    {
        double pitch = l.CellSizeMm + l.GapMm;
        double mx = 105 - l.WidthMm / 2 + c * pitch + l.CellSizeMm / 2, my = 105 - l.HeightMm / 2 + r * pitch + l.CellSizeMm / 2;
        var p = new PointD((mx - ViewX) * PxPerMm, (my - ViewY) * PxPerMm);
        return turned ? new PointD(W - p.X, H - p.Y) : p;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Corner_crosses_place_a_grid_whose_first_columns_did_not_mark(bool turned)
    {
        var grid = Grid();
        var job = GcodeExport.ToGcode(grid, cornerMarks: true);
        var img = Engrave(job);
        if (turned) img = Turn180(img);
        if (Environment.GetEnvironmentVariable("MOPACHROMA_DUMP") is { Length: > 0 } dir)
            img.SavePng(Path.Combine(dir, $"atlas-card{(turned ? "-turned" : "")}.png"));

        var d = GridDetect.Detect(img, grid.Layout.Rows, grid.Layout.Columns, marks: CardMarks.AtlasCorners(grid.Layout))!;
        Assert.NotNull(d);
        output.WriteLine($"turn {d.TurnDegrees}, {d.SquaresFound} squares, score {d.LabelScore:0.00}, certain {d.PlacementCertain}; {string.Join(" ", d.Notes)}");
        Assert.Equal(turned ? 180 : 0, d.TurnDegrees);
        Assert.True(d.PlacementCertain);
        double worst = 0;
        for (int r = 0; r < grid.Layout.Rows; r++)
            for (int c = 0; c < grid.Layout.Columns; c++)
                worst = Math.Max(worst, (d.Grid.Centre(r, c) - Truth(grid.Layout, c, r, turned)).Length);
        output.WriteLine($"worst cell {worst:0.00}px");
        Assert.True(worst < 2.0, $"worst cell {worst:0.00}px off");

        // The two weak columns read as bare metal; everything else as a colour.
        var m = ScanMeasure.Measure(img, d.Grid);
        foreach (var cell in grid.Cells)
            Assert.Equal(cell.Column < 2 ? Outcome.NoMark : Outcome.Good, m.At(cell.Row, cell.Column).Suggested);
    }

    [Fact]
    public void Without_crosses_the_same_card_cannot_be_placed_for_certain()
    {
        var grid = Grid();
        var img = Engrave(GcodeExport.ToGcode(grid, cornerMarks: false));
        var d = GridDetect.Detect(img, grid.Layout.Rows, grid.Layout.Columns, marks: CardMarks.AtlasCorners(grid.Layout));
        Assert.True(d is null || !d.PlacementCertain);
    }

    [Fact]
    public void Corner_crosses_stay_on_the_coupon_and_clear_of_the_cells()
    {
        var grid = Grid();
        var job = GcodeExport.ToGcode(grid, cornerMarks: true);
        var plain = GcodeExport.ToGcode(grid);
        Assert.StartsWith(plain[..plain.LastIndexOf("G0X105Y105", StringComparison.Ordinal)], job);

        // Every powered move of the crosses lies on the 86 × 54 blank and outside every cell.
        var l = grid.Layout;
        double x0 = 105 - l.WidthMm / 2, y0 = 105 - l.HeightMm / 2, pitch = l.CellSizeMm + l.GapMm;
        var extra = job[plain.LastIndexOf("G0X105Y105", StringComparison.Ordinal)..];
        double cx = 0, cy = 0;
        foreach (var raw in extra.Split('\n'))
        {
            double nx = Word(raw, 'X') ?? cx, ny = Word(raw, 'Y') ?? cy;
            if (raw.StartsWith("G1") && Word(raw, 'S') is > 0)
                foreach (var (px, py) in new[] { (cx, cy), (nx, ny) })
                {
                    Assert.InRange(px, 62 + 2, 148 - 2);
                    Assert.InRange(py, 78 + 2, 132 - 2);
                    bool inCell = px > x0 - 0.01 && px < x0 + l.WidthMm + 0.01 && py > y0 - 0.01 && py < y0 + l.HeightMm + 0.01
                                  && ((px - x0) % pitch) < l.CellSizeMm && ((py - y0) % pitch) < l.CellSizeMm;
                    Assert.False(inCell, $"cross point {px},{py} is inside a cell");
                }
            cx = nx; cy = ny;
        }
    }

    [Fact]
    public void Readings_fill_the_grids_pending_cells_and_never_overwrite_a_measurement()
    {
        var grid = Grid();
        var doc = new AtlasDocument();
        doc.Grids.Add(grid);
        foreach (var c in grid.Cells)
            doc.Samples.Add(new Sample { MaterialId = grid.MaterialId, SourceId = grid.SourceId, LensId = grid.LensId,
                                         GridId = grid.Id, CellLabel = c.Label, Settings = c.Settings });
        // One cell was already measured by hand: it must survive, with the reading appended.
        int handIdx = doc.Samples.FindIndex(s => s.CellLabel == "C2");
        doc.Samples[handIdx] = doc.Samples[handIdx] with { Outcome = Outcome.Good, Color = new MOPAChroma.Color.Lab(40, 1, 2) };

        var img = Engrave(GcodeExport.ToGcode(grid, cornerMarks: true));
        var d = GridDetect.Detect(img, 3, 8, marks: CardMarks.AtlasCorners(grid.Layout))!;
        var m = ScanMeasure.Measure(img, d.Grid);
        var capture = new CardCapture(img, CaptureKind.Photo, "Lumos Ultra camera", "test");
        var samples = GridReadout.ToSamples(grid, m, (r, c) => m.At(r, c).Suggested, capture, d.TurnDegrees, d.Grid.IsPerspective, "test");

        int filled = doc.RecordResults(samples);
        Assert.Equal(grid.Cells.Count - 1, filled);
        Assert.Equal(grid.Cells.Count + 1, doc.Samples.Count);
        Assert.DoesNotContain(doc.Samples, s => s.Outcome == Outcome.Pending);
        Assert.Equal(2, doc.Samples.Count(s => s.CellLabel == "C2"));
        Assert.All(doc.Samples.Where(s => s.HasColor && s.Notes.StartsWith("test")), s => Assert.Equal(MeasureMethod.Estimated, s.Method));
        // Each reading carries its own cell's recipe.
        foreach (var s in samples)
            Assert.Equal(grid.Cells.Single(c => c.Label == s.CellLabel).Settings, s.Settings);
    }

    [Fact]
    public void Every_grid_number_round_trips_and_a_single_wrong_dot_is_caught()
    {
        for (int n = 1; n <= CardId.MaxNumber; n++)
        {
            var bits = CardId.Encode(n);
            Assert.Equal(n, CardId.Decode(bits));
            // Flip each data/check dot in turn: never read as another number.
            for (int k = 1; k < CardId.Positions - 1; k++)
            {
                var bad = (bool[])bits.Clone();
                bad[k] = !bad[k];
                Assert.Null(CardId.Decode(bad));
            }
        }
    }

    [Theory]
    [InlineData(false, 1234)]
    [InlineData(true, 1234)]
    [InlineData(false, 29)]
    [InlineData(true, 4095)]
    public void The_engraved_grid_number_is_read_back(bool turned, int number)
    {
        var grid = Grid() with { Number = number };
        var img = Engrave(GcodeExport.ToGcode(grid, cornerMarks: true));
        if (turned) img = Turn180(img);
        if (Environment.GetEnvironmentVariable("MOPACHROMA_DUMP") is { Length: > 0 } dir)
            img.SavePng(Path.Combine(dir, $"atlas-card-id{(turned ? "-turned" : "")}.png"));
        var d = GridDetect.Detect(img, grid.Layout.Rows, grid.Layout.Columns, marks: CardMarks.AtlasCorners(grid.Layout))!;
        Assert.True(d.PlacementCertain);
        Assert.Equal(number, CardId.Read(img, d.Grid, grid.Layout));
    }

    [Fact]
    public void A_card_without_a_number_reads_as_none()
    {
        var grid = Grid();
        var img = Engrave(GcodeExport.ToGcode(grid, cornerMarks: true));
        var d = GridDetect.Detect(img, grid.Layout.Rows, grid.Layout.Columns, marks: CardMarks.AtlasCorners(grid.Layout))!;
        Assert.Null(CardId.Read(img, d.Grid, grid.Layout));
    }
}
