using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Scanning;
using Xunit.Abstractions;

namespace MOPAChroma.Tests;

public class ScanTests(ITestOutputHelper output)
{
    private const int Rows = 10, Cols = 20;
    private static readonly Srgb8 BareMetal = new(10, 10, 12);

    /// <summary>
    /// A synthetic card: pitch 40 px, squares 32 px, tilted 1.5°. Columns 0-8 are dark
    /// (too little contrast to vote in the fit), 9-19 bright and colourful, and row 9
    /// columns 0-2 are left unengraved (bare metal).
    /// </summary>
    private static (RgbImage img, Func<int, int, PointD> truth, Func<int, int, Srgb8> colour) MakeCard()
    {
        const double pitch = 40, side = 32, tilt = 1.5 * Math.PI / 180;
        var origin = new PointD(120, 90);
        var u = new PointD(Math.Cos(tilt), Math.Sin(tilt)) * pitch;
        var v = new PointD(-Math.Sin(tilt), Math.Cos(tilt)) * pitch;
        PointD Truth(int r, int c) => origin + u * c + v * r;

        Srgb8 Colour(int r, int c) =>
            r == 9 && c < 3 ? BareMetal
            : c < 9 ? new Srgb8(22, 21, 24)
            : new Srgb8((byte)(40 + 8 * c), (byte)(60 + 10 * r), (byte)(200 - 7 * c));

        int w = 1100, h = 600;
        var px = new byte[w * h * 3];
        double cos = Math.Cos(tilt), sin = Math.Sin(tilt);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Undo the tilt to get grid coordinates in cell units.
                double dx = x + 0.5 - origin.X, dy = y + 0.5 - origin.Y;
                double gc = (dx * cos + dy * sin) / pitch, gr = (-dx * sin + dy * cos) / pitch;
                int c = (int)Math.Round(gc), r = (int)Math.Round(gr);
                var col = BareMetal;
                if (r >= 0 && r < Rows && c >= 0 && c < Cols
                    && Math.Abs(gc - c) * pitch <= side / 2 && Math.Abs(gr - r) * pitch <= side / 2)
                    col = Colour(r, c);
                int i = (y * w + x) * 3;
                px[i] = col.R; px[i + 1] = col.G; px[i + 2] = col.B;
            }
        return (new RgbImage(w, h, px), Truth, Colour);
    }

    [Fact]
    public void Fit_recovers_a_tilted_grid_from_rough_clicks()
    {
        var (img, truth, _) = MakeCard();
        // Clicks off by up to 7 px (a sixth of a cell) in different directions.
        var start = GridGeometry.FromCorners(
            truth(0, 0) + new PointD(6, -5), truth(0, Cols - 1) + new PointD(-7, 6),
            truth(Rows - 1, 0) + new PointD(5, 7), Rows, Cols);

        var fit = GridFit.Refine(img, start);
        output.WriteLine($"rms {fit.RmsPixels:0.00}px from {fit.CellsUsed} cells, tilt {fit.Grid.TiltDegrees:0.00}°");

        Assert.True(fit.CellsUsed >= 100, $"only {fit.CellsUsed} cells voted");
        Assert.True(fit.RmsPixels < 1.0);
        Assert.InRange(fit.Grid.TiltDegrees, 1.4, 1.6);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                Assert.True((fit.Grid.Centre(r, c) - truth(r, c)).Length < 1.0, $"cell {r},{c} misplaced");
    }

    [Fact]
    public void Measure_reads_colours_and_finds_bare_metal()
    {
        var (img, truth, colour) = MakeCard();
        var grid = GridFit.Refine(img, GridGeometry.FromCorners(truth(0, 0), truth(0, Cols - 1), truth(Rows - 1, 0), Rows, Cols)).Grid;
        var m = ScanMeasure.Measure(img, grid);

        Assert.True(DeltaE.Cie2000(m.Bare, ColorConvert.ToLab(BareMetal)) < 0.5);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                var cell = m.At(r, c);
                Assert.Equal(colour(r, c), cell.Rgb);
                Assert.True(cell.Spread < 1);
            }
        Assert.Equal(Outcome.NoMark, m.At(9, 0).Suggested);
        Assert.Equal(Outcome.Good, m.At(0, 15).Suggested);
    }

    [Fact]
    public void Colour_test_definition_maps_the_card_in_standard_orientation()
    {
        var def = ColorTestDefinition.UltraColourTest(); // Lee's first card: 150-350 kHz x 10, 40-90 % x 20
        Assert.Null(def.Problem());
        Assert.Equal(350, def.SettingsAt(0, 0).FrequencyKHz, 6);   // top row
        Assert.Equal(150, def.SettingsAt(9, 0).FrequencyKHz, 6);   // bottom row
        Assert.Equal(40, def.SettingsAt(0, 0).PowerPercent, 6);    // left
        Assert.Equal(90, def.SettingsAt(0, 19).PowerPercent, 6);   // right
        Assert.Equal(800, def.SettingsAt(4, 7).SpeedMmPerS, 6);    // fixed
        // Exact linear step is 42.632; confirmed from real G-code that MakeIt rounds power
        // to the nearest whole percent before sending it to the laser, matching its label.
        Assert.Equal(43, def.SettingsAt(0, 1).PowerPercent, 6);

        var grid = def.ToGrid("t", "m", SourceProfile.Presets[0], LensProfile.Presets[0]);
        Assert.Equal(200, grid.Cells.Count);
        Assert.Equal((0, 0), (grid.Cells[0].Row, grid.Cells[0].Column));
        Assert.Equal(350, grid.Cells[0].Settings.FrequencyKHz, 6);
    }

    /// <summary>
    /// Lee's real scan, when it is present on this machine (scans are never committed).
    /// Corners are the hand-read centres used in tools/scan_probe.py.
    /// </summary>
    [Fact]
    public void Real_scan_fits_when_available()
    {
        var path = FindRepoFile(Path.Combine("samples", "scans", "0 degrees.png"));
        if (path is null) { output.WriteLine("scan not present - skipped"); return; }

        var img = RgbImage.Load(path);
        var start = GridGeometry.FromCorners(new(555, 517), new(2167, 469), new(569, 1279), Rows, Cols);
        var fit = GridFit.Refine(img, start);
        var m = ScanMeasure.Measure(img, fit.Grid);
        output.WriteLine($"{img.Width}x{img.Height} @ {img.Dpi:0} dpi; rms {fit.RmsPixels:0.00}px from {fit.CellsUsed} cells; tilt {fit.Grid.TiltDegrees:0.00}°; bare {m.Bare}");
        output.WriteLine($"suggested: {string.Join(", ", m.Cells.GroupBy(c => c.Suggested).Select(g => $"{g.Key} {g.Count()}"))}");

        Assert.True(fit.CellsUsed >= 60);
        Assert.True(fit.RmsPixels < 2.0);
    }

    private static string? FindRepoFile(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "MOPAChromaAtlas.slnx")))
            {
                var p = Path.Combine(d.FullName, relative);
                return File.Exists(p) ? p : null;
            }
        return null;
    }
}
