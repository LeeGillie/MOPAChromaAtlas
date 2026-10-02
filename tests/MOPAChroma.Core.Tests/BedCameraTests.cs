using System.Globalization;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Scanning;
using Xunit.Abstractions;

namespace MOPAChroma.Tests;

public class BedCameraTests(ITestOutputHelper output)
{
    /// <summary>
    /// A made-up camera like the Ultra's: picture turned a quarter, looking down at a slight
    /// tilt, with barrel distortion from a wide lens (k1 on normalised radius).
    /// </summary>
    private static PointD Camera(PointD bed, double k1)
    {
        // Bed mm -> ideal pinhole picture (4032 x 3024, ~16 px/mm, turned 90°, tilted).
        double x = bed.Y, y = 210 - bed.X;
        double w = 1 + 0.0006 * (x - 105);
        var p = new PointD(2016 + (x - 105) * 16.5 / w, 1512 + (y - 105) * 16.5 / w);
        // Barrel distortion about the picture centre.
        double nx = (p.X - 2016) / 2016, ny = (p.Y - 1512) / 2016, r2 = nx * nx + ny * ny;
        double f = 1 + k1 * r2;
        return new PointD(2016 + nx * f * 2016, 1512 + ny * f * 2016);
    }

    [Theory]
    [InlineData(0.0, 0.05)]
    [InlineData(-0.04, 0.4)]
    [InlineData(0.03, 0.4)]
    public void Nine_target_crosses_map_the_whole_bed(double k1, double allowedMm)
    {
        var bed = BedCameraTarget.Points;
        var img = bed.Select(b => Camera(b, k1) + new PointD(0.4, -0.3)).ToArray(); // a click is never exact
        var cam = BedCamera.Fit(bed, img, 4032, 3024, DateTimeOffset.UtcNow)!;
        Assert.NotNull(cam);
        double worst = 0;
        for (double x = 15; x <= 195; x += 15)
            for (double y = 15; y <= 195; y += 15)
            {
                var b = new PointD(x, y);
                worst = Math.Max(worst, (cam.ToBed(Camera(b, k1)) - b).Length);
                // And the two directions agree.
                Assert.True((cam.ToBed(cam.ToImage(b)) - b).Length < 0.01);
            }
        output.WriteLine($"k1 {k1}: fit rms {cam.RmsMm:0.00} mm, max {cam.MaxMm:0.00}; worst over the bed {worst:0.00} mm");
        Assert.True(worst < allowedMm, $"worst {worst:0.00} mm");
    }

    /// <summary>
    /// Clicked in a random order, in a picture that is turned or mirrored any way, the crosses
    /// are matched to the right target points - and the calibration maps the bed, not its mirror.
    /// </summary>
    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(1, false, 2)]
    [InlineData(2, true, 3)]
    [InlineData(3, true, 4)]
    public void Crosses_clicked_in_any_order_are_matched(int quarterTurns, bool mirror, int seed)
    {
        var rnd = new Random(seed);
        int w = 4032, h = 3024;
        for (int t = 0; t < quarterTurns; t++) (w, h) = (h, w);
        PointD Pic(PointD bed)
        {
            // Turn / mirror the whole picture as the orientation buttons do (RgbImage.Oriented).
            var p = Camera(bed, -0.04);
            int pw = 4032, ph = 3024;
            for (int t = 0; t < quarterTurns; t++) { p = new PointD(ph - p.Y, p.X); (pw, ph) = (ph, pw); }
            return mirror ? new PointD(pw - p.X, p.Y) : p;
        }
        var order = Enumerable.Range(0, BedCameraTarget.Points.Length).OrderBy(_ => rnd.Next()).ToArray();
        var clicks = order.Select(i => Pic(BedCameraTarget.Points[i]) + new PointD(rnd.NextDouble() * 3 - 1.5, rnd.NextDouble() * 3 - 1.5)).ToArray();
        var m = BedCameraTarget.Match(clicks, w, h, DateTimeOffset.UtcNow)!;
        Assert.NotNull(m);
        Assert.Equal(order, m.Index);
        output.WriteLine($"turns {quarterTurns} mirror {mirror}: rms {m.Camera.RmsMm:0.00} mm, margin {m.Margin:0.0} mm");
        Assert.True(m.Margin > 3, $"margin {m.Margin:0.0}");
        // A bed point well away from every cross lands where it should.
        var b = new PointD(150, 150);
        Assert.True((m.Camera.ToBed(Pic(b)) - b).Length < 0.5);
    }

    [Fact]
    public void A_stray_click_is_left_out()
    {
        var clicks = BedCameraTarget.Points.Take(7).Select(p => Camera(p, 0)).Append(new PointD(100, 100)).ToArray();
        var m = BedCameraTarget.Match(clicks, 4032, 3024, DateTimeOffset.UtcNow)!;
        output.WriteLine(string.Join(",", m.Index));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, -1], m.Index);
    }

    [Fact]
    public void Calibration_target_stays_on_the_bed_with_one_to_nine_bars()
    {
        var g = GcodeExport.CalibrationTarget(new MopaSettings { PowerPercent = 50, SpeedMmPerS = 500, FrequencyKHz = 40, PulseWidthNs = 200 });
        if (Environment.GetEnvironmentVariable("MOPACHROMA_DUMP") is { Length: > 0 } dir) File.WriteAllText(Path.Combine(dir, "target.gc"), g);
        var strokes = new List<(double x0, double y0, double x1, double y1)>();
        double cx = 0, cy = 0;
        foreach (var line in g.Split('\n'))
        {
            double nx = Word(line, 'X') ?? cx, ny = Word(line, 'Y') ?? cy;
            if (line.StartsWith("G1") && Word(line, 'S') is > 0) strokes.Add((cx, cy, nx, ny));
            cx = nx; cy = ny;
        }
        Assert.All(strokes, s => { Assert.InRange(s.x0, 5, 205); Assert.InRange(s.x1, 5, 205); Assert.InRange(s.y0, 5, 205); Assert.InRange(s.y1, 5, 205); });
        // Short vertical strokes (bars), three lines each, grouped by cross.
        for (int i = 0; i < BedCameraTarget.Points.Length; i++)
        {
            var p = BedCameraTarget.Points[i];
            int bars = strokes.Count(s => Math.Abs(s.y1 - s.y0 - BedCameraTarget.BarLengthMm) < 0.01 && Math.Abs(s.x0 - p.X) < 20 && Math.Abs((s.y0 + s.y1) / 2 - p.Y) < 0.01);
            Assert.Equal(3 * (i + 1), bars);
        }
    }

    [Fact]
    public void Extents_cover_cells_and_crosses()
    {
        var l = new GridLayout { Columns = 8, Rows = 3, CellSizeMm = 8, GapMm = 2 };
        var grid = new TestGrid { Title = "t", MaterialId = "m", SourceId = "", LensId = "", Layout = l, Cells = [] };
        var (x0, y0, x1, y1) = GcodeExport.Extents(grid, 105, 105);
        Assert.Equal(105 - l.WidthMm / 2, x0, 6);
        Assert.Equal(105 + l.HeightMm / 2, y1, 6);
        var m = GcodeExport.Extents(grid, 105, 105, cornerMarks: true);
        Assert.True(m.MinY < y0 && m.MaxY > y1);
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
}
