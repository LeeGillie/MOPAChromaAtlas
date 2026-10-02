using MOPAChroma.Scanning;
using Xunit.Abstractions;

namespace MOPAChroma.Tests;

public class GridDetectTests
{
    private readonly ITestOutputHelper output;
    public GridDetectTests(ITestOutputHelper output) { this.output = output; GridDetect.Trace = output.WriteLine; }

    // ---------------------------------------------------------------- homography

    [Fact]
    public void Homography_fit_recovers_a_perspective_map()
    {
        var truth = new Homography([1.9, 0.2, 100, -0.15, 2.1, 80, 0.0004, -0.0003, 1]);
        var src = new List<PointD>();
        for (int c = 0; c < 20; c++)
            for (int r = 0; r < 10; r++) src.Add(new PointD(c * 40, r * 40));
        var dst = src.Select(truth.Map).ToList();

        var fit = Homography.Fit(src, dst)!;
        foreach (var p in src) Assert.True((fit.Map(p) - truth.Map(p)).Length < 1e-6);

        var inv = fit.Inverse()!;
        foreach (var p in src) Assert.True((inv.Map(fit.Map(p)) - p).Length < 1e-6);
    }

    [Fact]
    public void Affine_fit_of_an_affine_map_is_exact_and_flagged_affine()
    {
        var truth = Homography.Affine(new PointD(50, 60), new PointD(40, 1), new PointD(-1, 40));
        var src = new List<PointD> { new(0, 0), new(5, 0), new(0, 3), new(5, 3), new(2, 1) };
        var fit = Homography.Fit(src, src.Select(truth.Map).ToList(), affineOnly: true)!;
        Assert.True(fit.IsAffine);
        Assert.True((fit.Map(3, 2) - truth.Map(3, 2)).Length < 1e-9);
    }

    // ---------------------------------------------------------------- synthetic cards

    /// <summary>
    /// A labelled synthetic card: <paramref name="rows"/> × <paramref name="cols"/> squares on
    /// dark metal, a value label left of every row and under every column (as MakeIt does),
    /// optionally with the first <paramref name="faintColumns"/> columns barely visible.
    /// Drawn through <paramref name="cardToImage"/> (card mm → image px), so it can be
    /// turned, scaled or photographed at an angle.
    /// </summary>
    private static RgbImage DrawCard(int w, int h, int rows, int cols, Homography cardToImage, int faintColumns = 0,
                                     bool labels = true, double light = 0)
    {
        const double pitch = 4.0, side = 3.0;
        var inv = cardToImage.Inverse()!;
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var m = inv.Map(x + 0.5, y + 0.5); // card millimetres; cell (c, r) centred at (c·pitch, r·pitch)
                (byte R, byte G, byte B) col = (14, 14, 18);
                bool onCard = m.X > -12 && m.X < cols * pitch + 4 && m.Y > -6 && m.Y < rows * pitch + 8;
                if (!onCard) col = (235, 235, 230); // paper around the card
                double gc = m.X / pitch, gr = m.Y / pitch;
                int c = (int)Math.Round(gc), r = (int)Math.Round(gr);
                if (r >= 0 && r < rows && c >= 0 && c < cols &&
                    Math.Abs(gc - c) * pitch <= side / 2 && Math.Abs(gr - r) * pitch <= side / 2)
                    col = c < faintColumns ? ((byte)22, (byte)22, (byte)26)
                        : ((byte)(60 + 7 * c % 150), (byte)(90 + 9 * r % 120), (byte)(170 - 5 * c % 100));
                if (labels && onCard)
                {
                    // "Text": a stroked 3-digit-ish glyph pattern left of each row and below each column.
                    bool rowLabel = r >= 0 && r < rows && m.X > -6.6 && m.X < -2.6 && Math.Abs(gr - r) * pitch < 1.0; // "350": about a cell wide
                    bool colLabel = c >= 0 && c < cols && m.Y > rows * pitch - 1.6 && m.Y < rows * pitch + 1.4 && Math.Abs(gc - c) * pitch < 1.3;
                    // Thin strokes, like engraved digits: never square blobs themselves.
                    if ((rowLabel || colLabel) && (int)Math.Floor(m.Y * 3.4) % 2 == 0 && (int)Math.Floor(m.X * 1.2) % 2 == 0)
                        col = (150, 140, 120);
                }
                double shade = 1 + light * (x / (double)w - 0.5); // uneven lighting across a photo
                int i = (y * w + x) * 3;
                px[i] = Clamp(col.R * shade); px[i + 1] = Clamp(col.G * shade); px[i + 2] = Clamp(col.B * shade);
            }
        return new RgbImage(w, h, px);
    }

    /// <summary>Set MOPACHROMA_DUMP to a folder to save the synthetic images for inspection.</summary>
    private static RgbImage Dump(RgbImage img, string name)
    {
        if (Environment.GetEnvironmentVariable("MOPACHROMA_DUMP") is { Length: > 0 } dir)
        {
            using var im = SixLabors.ImageSharp.Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgb24>(img.Pixels, img.Width, img.Height);
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(im, Path.Combine(dir, name + ".png"));
        }
        return img;
    }

    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>Card (column, row) → image pixels for the drawing above.</summary>
    private static Homography CellToImage(Homography cardMmToImage) =>
        Homography.Affine(new PointD(0, 0), new PointD(4, 0), new PointD(0, 4)).Then(cardMmToImage);

    private void AssertMatches(GridDetection d, Homography cellTruth, int rows, int cols, double tolPx)
    {
        output.WriteLine($"turn {d.TurnDegrees}°, {d.SquaresFound} squares found, label score {d.LabelScore:0.00}, " +
                         $"certain {d.PlacementCertain}, fit {d.RmsPixels:0.00}px from {d.CellsUsed}; {string.Join(" ", d.Notes)}");
        Assert.Equal(rows, d.Grid.Rows);
        Assert.Equal(cols, d.Grid.Columns);
        double worst = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                worst = Math.Max(worst, (d.Grid.Centre(r, c) - cellTruth.Map(c, r)).Length);
        output.WriteLine($"worst cell error {worst:0.00}px");
        Assert.True(worst < tolPx, $"worst cell {worst:0.00}px off");
    }

    [Fact]
    public void Finds_a_flat_labelled_card_without_clicks()
    {
        var cardToImg = Homography.Affine(new PointD(120, 90), new PointD(10, 0.25), new PointD(-0.25, 10));
        var img = DrawCard(1100, 640, 10, 20, cardToImg);
        var d = GridDetect.Detect(img, 10, 20)!;
        Assert.NotNull(d);
        Assert.Equal(0, d.TurnDegrees);
        Assert.True(d.PlacementCertain);
        AssertMatches(d, CellToImage(cardToImg), 10, 20, 1.0);
    }

    [Fact]
    public void Uses_the_labels_to_place_a_grid_whose_first_columns_are_too_faint_to_see()
    {
        var cardToImg = Homography.Affine(new PointD(140, 90), new PointD(10, 0), new PointD(0, 10));
        var img = Dump(DrawCard(1100, 640, 10, 20, cardToImg, faintColumns: 4), "faint");
        var d = GridDetect.Detect(img, 10, 20)!;
        Assert.True(d.PlacementCertain);
        AssertMatches(d, CellToImage(cardToImg), 10, 20, 1.0);
    }

    [Fact]
    public void Works_out_that_the_card_is_turned()
    {
        // Card turned 90° anticlockwise: its top edge runs down the image's left side.
        var cardToImg = Homography.Affine(new PointD(120, 950), new PointD(0, -10), new PointD(10, 0));
        var img = DrawCard(640, 1100, 10, 20, cardToImg);
        var d = GridDetect.Detect(img, 10, 20)!;
        Assert.Equal(270, d.TurnDegrees);
        AssertMatches(d, CellToImage(cardToImg), 10, 20, 1.0);
    }

    [Fact]
    public void Corrects_perspective_and_uneven_light_in_a_photo()
    {
        // Card photographed from below and to one side: the far end ~30 % smaller.
        var cardToImg = new Homography([9.5, 1.2, 170, -0.6, 9.0, 140, 0.0016, 0.0022, 1]);
        var img = Dump(DrawCard(1300, 900, 10, 20, cardToImg, light: 0.6), "perspective");
        var d = GridDetect.Detect(img, 10, 20)!;
        Assert.True(d.Grid.IsPerspective);
        AssertMatches(d, CellToImage(cardToImg), 10, 20, 1.5);
    }

    [Fact]
    public void Works_out_rows_and_columns_when_not_told()
    {
        var cardToImg = Homography.Affine(new PointD(130, 100), new PointD(11, 0), new PointD(0, 11));
        var img = DrawCard(900, 600, 7, 13, cardToImg);
        var d = GridDetect.Detect(img)!;
        AssertMatches(d, CellToImage(cardToImg), 7, 13, 1.0);
    }

    [Fact]
    public void Flags_an_unlabelled_card_it_cannot_place_for_certain()
    {
        var cardToImg = Homography.Affine(new PointD(140, 90), new PointD(10, 0), new PointD(0, 10));
        var img = DrawCard(1100, 640, 10, 20, cardToImg, faintColumns: 3, labels: false);
        var d = GridDetect.Detect(img, 10, 20)!;
        Assert.False(d.PlacementCertain);
        Assert.Contains(d.Notes, n => n.Contains("check the boxes"));
    }

    // ---------------------------------------------------------------- Lee's real scans

    private static string? Sample(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "samples", "scans", name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// The first real card (mirror stainless, 10 frequency rows × 20 power columns), scanned
    /// in the standard orientation and turned 90°. Truth is the existing three-click fit;
    /// the clicks are the centres of the corner squares read off the scans. Skipped when the
    /// scans are not present (they are never committed).
    /// </summary>
    [Theory]
    [InlineData("0 degrees.png", 0, 567, 523, 2167, 489, 571, 1273)]
    [InlineData("90 degrees.png", 270, 593, 2038, 593, 439, 1356, 2038)]
    public void Finds_the_real_scanned_card_without_clicks(string file, int turn, double tlx, double tly, double trx, double try_, double blx, double bly)
    {
        if (Sample(file) is not { } path) { output.WriteLine($"{file} not present: skipped"); return; }
        var img = RgbImage.Load(path);
        var truth = GridFit.Refine(img, GridGeometry.FromCorners(new(tlx, tly), new(trx, try_), new(blx, bly), 10, 20)).Grid;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var d = GridDetect.Detect(img, 10, 20)!;
        output.WriteLine($"{file}: detected in {sw.ElapsedMilliseconds} ms");
        Assert.NotNull(d);
        Assert.Equal(turn, d.TurnDegrees);
        Assert.True(d.PlacementCertain);
        AssertMatches(d, truth.AsWarp(), 10, 20, 2.0);
    }

    [Fact]
    public void Finds_the_real_card_in_a_simulated_angled_photo_of_the_scan()
    {
        if (Sample("0 degrees.png") is not { } path) { output.WriteLine("scan not present: skipped"); return; }
        var scan = RgbImage.Load(path);
        var truth = GridFit.Refine(scan, GridGeometry.FromCorners(new(567, 523), new(2167, 489), new(571, 1273), 10, 20)).Grid;

        // Re-photograph the scan: perspective, a slight turn, and light falling off to one side.
        var scanToPhoto = new Homography([0.72, 0.09, 140, -0.05, 0.74, 160, 0.00011, 0.00009, 1]);
        var photoToScan = scanToPhoto.Inverse()!;
        int w = 2400, h = 1700;
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var s = photoToScan.Map(x + 0.5, y + 0.5);
                int sx = (int)s.X, sy = (int)s.Y, i = (y * w + x) * 3;
                double shade = 0.75 + 0.45 * x / w;
                if (sx < 0 || sy < 0 || sx >= scan.Width || sy >= scan.Height) { px[i] = px[i + 1] = px[i + 2] = 200; continue; }
                int j = (sy * scan.Width + sx) * 3;
                for (int k = 0; k < 3; k++) px[i + k] = Clamp(scan.Pixels[j + k] * shade);
            }
        var photo = new RgbImage(w, h, px);

        var d = GridDetect.Detect(photo, 10, 20)!;
        Assert.NotNull(d);
        Assert.Equal(0, d.TurnDegrees);
        Assert.True(d.Grid.IsPerspective);
        AssertMatches(d, truth.AsWarp().Then(scanToPhoto), 10, 20, 2.5);
    }
}
