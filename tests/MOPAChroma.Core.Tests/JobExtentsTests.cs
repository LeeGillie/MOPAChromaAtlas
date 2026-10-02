using System.Globalization;
using MOPAChroma.Grids;
using MOPAChroma.Laser;

namespace MOPAChroma.Tests;

/// <summary>
/// The outline Send to laser draws ("will engrave here") is GcodeExport.Extents. Everything
/// the laser fires at (cells, corner crosses, grid-number dots) must lie inside it, for every
/// grid shape and number, or a job placed by its outline could land partly off the blank.
/// Checked against the actual G-code, not against the layout arithmetic.
/// </summary>
public class JobExtentsTests
{
    private static TestGrid Grid(int cols, int rows, double cell, double gap, int number) => new()
    {
        Title = "x", MaterialId = "m", SourceId = SourceProfile.Presets[0].Id, LensId = LensProfile.Presets[0].Id,
        Number = number,
        Layout = new GridLayout { Columns = cols, Rows = rows, CellSizeMm = cell, GapMm = gap },
        Cells = Enumerable.Range(0, cols * rows).Select(i => new GridCell(i / cols, i % cols, GridLayout.Label(i / cols, i % cols),
            new MopaSettings { PowerPercent = 30 + i % 60, SpeedMmPerS = 500, LineDensityPerCm = 300, FrequencyKHz = 200, PulseWidthNs = 60, Passes = 1 + i % 3 })).ToList(),
    };

    private static double? Word(string line, char letter)
    {
        int i = line.IndexOf(letter, 1);
        if (i < 0) return null;
        int j = i + 1;
        while (j < line.Length && (char.IsDigit(line[j]) || line[j] is '.' or '-')) j++;
        return j > i + 1 ? double.Parse(line[(i + 1)..j], CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Every point the beam is on at: both ends of each powered G1 move.</summary>
    private static IEnumerable<(double X, double Y)> FiredPoints(string gcode)
    {
        double x = 0, y = 0;
        foreach (var raw in gcode.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('G')) continue;
            double nx = Word(line, 'X') ?? x, ny = Word(line, 'Y') ?? y;
            if (line.StartsWith("G1") && Word(line, 'S') is > 0)
            {
                yield return (x, y);
                yield return (nx, ny);
            }
            x = nx; y = ny;
        }
    }

    [Theory]
    [InlineData(8, 3, 8.0, 2.0)]   // the usual business-card grid
    [InlineData(9, 5, 4.0, 1.0)]   // a micro-grid
    [InlineData(2, 2, 3.0, 0.5)]   // as small as a grid gets (too narrow for a number)
    [InlineData(3, 2, 2.0, 0.5)]   // tiny cells
    [InlineData(20, 10, 10.0, 3.0)] // a large sheet grid
    [InlineData(12, 4, 6.0, 1.5)]
    public void Everything_the_laser_fires_at_is_inside_the_outline(int cols, int rows, double cell, double gap)
    {
        foreach (int number in new[] { 0, 1, 1234, 2730, CardId.MaxNumber })
            foreach (var (cx, cy) in new[] { (105.0, 105.0), (40.0, 170.0) })
            {
                var g = Grid(cols, rows, cell, gap, number);
                var (x0, y0, x1, y1) = GcodeExport.Extents(g, cx, cy, cornerMarks: true);
                var pts = FiredPoints(GcodeExport.ToGcode(g, cx, cy, cornerMarks: true)).ToList();
                Assert.NotEmpty(pts);
                foreach (var (x, y) in pts)
                    Assert.True(x >= x0 - 1e-6 && x <= x1 + 1e-6 && y >= y0 - 1e-6 && y <= y1 + 1e-6,
                        $"{cols}x{rows} cell {cell} #{number}: fires at ({x:0.###},{y:0.###}) outside ({x0:0.###},{y0:0.###})-({x1:0.###},{y1:0.###})");
            }
    }

    [Fact]
    public void Without_crosses_the_outline_is_the_cells()
    {
        var g = Grid(8, 3, 8, 2, 77);
        var (x0, y0, x1, y1) = GcodeExport.Extents(g, 105, 105, cornerMarks: false);
        Assert.All(FiredPoints(GcodeExport.ToGcode(g, 105, 105, cornerMarks: false)),
            p => Assert.True(p.X >= x0 - 1e-6 && p.X <= x1 + 1e-6 && p.Y >= y0 - 1e-6 && p.Y <= y1 + 1e-6));
    }
}
