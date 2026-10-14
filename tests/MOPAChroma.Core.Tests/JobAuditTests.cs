using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Grids;
using LumosUltra.Jobs;
using MOPAChroma.Planning;

namespace MOPAChroma.Tests;

/// <summary>
/// The last check before anything reaches the laser (<see cref="JobAudit"/>): computed from the
/// G-code alone. The job that cut through the sheet on 2026-10-01 must be refused; the heat it
/// reports must match the physics; and every ordinary job Atlas makes must pass.
/// </summary>
public class JobAuditTests
{
    private static readonly SourceProfile Src = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];
    private const double W = 100; // the check always uses the highest rated power known

    private static TestGrid OneCell(MopaSettings s) => new()
    {
        Title = "t", MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id, Number = 7,
        Layout = new GridLayout { Columns = 2, Rows = 1, CellSizeMm = 8, GapMm = 2 },
        Cells = [new GridCell(0, 0, "A1", s), new GridCell(0, 1, "B1", s with { PowerPercent = 5 })],
    };

    [Fact]
    public void The_job_that_cut_through_is_refused()
    {
        var burn = new MopaSettings { PowerPercent = 90, SpeedMmPerS = 50, LineDensityPerCm = 3000, FrequencyKHz = 500, PulseWidthNs = 200, Passes = 3 };
        var r = JobAudit.Check(GcodeExport.ToGcode(OneCell(burn), cornerMarks: true), W);
        Assert.False(r.Ok);
        Assert.True(r.MaxJPerMm2 > 1000, $"{r.MaxJPerMm2:0}");
        Assert.StartsWith("Not sent", r.Message);
    }

    [Theory]
    [InlineData(150, 1)]
    [InlineData(300, 1)]
    [InlineData(1000, 2)]
    [InlineData(3000, 3)]
    public void It_measures_the_same_heat_per_area_as_the_planner(int linesPerCm, int passes)
    {
        // Half power, speed chosen so the cell is exactly 20 J/mm² by the planner's formula.
        var s = new MopaSettings { PowerPercent = 50, LineDensityPerCm = linesPerCm, FrequencyKHz = 300, PulseWidthNs = 200, Passes = passes, SpeedMmPerS = 1 };
        s = s with { SpeedMmPerS = 0.5 * W * passes / (20 * s.LinePitchMm) };
        Assert.Equal(20, HeatLimits.ArealEnergy(s, Src, Lens), 6);
        var r = JobAudit.Check(GcodeExport.ToGcode(OneCell(s)), W);
        Assert.True(r.Ok);
        Assert.InRange(r.MaxJPerMm2, 20 * 0.97, 20 * 1.07);
    }

    [Fact]
    public void Ordinary_grids_with_their_marks_pass()
    {
        var planner = new GridPlanner(ParameterSpace.DefaultFor(Src), Src, Lens, poolSize: 2000);
        var layout = new GridLayout { Columns = 8, Rows = 5, CellSizeMm = 8, GapMm = 2 };
        var seed = planner.Seed(layout.Capacity);
        var g = TestGrid.Arrange("seed", "Seed", layout, seed.Select(p => (p.Settings, p.Reason)), Src, Lens, "m") with { Number = 12 };
        var r = JobAudit.Check(GcodeExport.ToGcode(g, cornerMarks: true), W);
        Assert.True(r.Ok, r.Message);

        // A refinement after mixed results, and a micro-grid around its hottest good cell.
        var rnd = new Random(3);
        var samples = seed.Select(p => new Sample
        {
            MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id, GridId = g.Id, Settings = p.Settings,
            Outcome = rnd.NextDouble() < 0.25 ? Outcome.NoMark : Outcome.Good, Color = new Lab(50, 10, -30),
        }).ToList();
        var refine = TestGrid.Arrange("r", "Refine", layout, planner.Refine(layout.Capacity, samples).Select(p => (p.Settings, p.Reason)), Src, Lens, "m") with { Number = 13 };
        Assert.True(JobAudit.Check(GcodeExport.ToGcode(refine, cornerMarks: true), W).Ok);
        var hot = samples.Where(x => x.Outcome == Outcome.Good).MaxBy(x => HeatLimits.ArealEnergy(x.Settings, Src, Lens))!.Settings;
        double ceiling = HeatLimits.Ceiling(samples, Src, Lens);
        var micro = MicroGrid.AtlasCells(hot, Src, samples.Select(x => x.Settings), layout.Capacity, allowed: s => HeatLimits.ArealEnergy(s, Src, Lens) <= ceiling);
        var mg = TestGrid.Arrange("m", "Micro", layout, micro, Src, Lens, "m") with { Number = 14 };
        Assert.True(JobAudit.Check(GcodeExport.ToGcode(mg, cornerMarks: true), W).Ok);
    }

    [Fact]
    public void Marks_borrowing_a_cell_at_the_limit_stay_under_it()
    {
        // The hottest a cell may be, at the widest line spacing: the dots and crosses borrow its
        // power and speed, and must not come out hotter per area than the cell itself.
        var s = new MopaSettings { SpeedMmPerS = 50, LineDensityPerCm = 150, FrequencyKHz = 300, PulseWidthNs = 200, Passes = 1, PowerPercent = 1 };
        s = s with { PowerPercent = HeatLimits.AbsoluteMaxJPerMm2 * 50 * s.LinePitchMm / W * 100 };
        var r = JobAudit.Check(GcodeExport.ToGcode(OneCell(s), cornerMarks: true), W);
        Assert.True(r.Ok, r.Message);
    }

    [Fact]
    public void The_camera_calibration_target_passes()
    {
        var target = GcodeExport.CalibrationTarget(new MopaSettings { PowerPercent = 40, SpeedMmPerS = 500, FrequencyKHz = 40, PulseWidthNs = 200 });
        Assert.True(JobAudit.Check(target, W).Ok);
    }

    [Fact]
    public void A_job_it_cannot_read_is_refused()
    {
        Assert.False(JobAudit.Check("G1X10Y10S500\n", W).Ok);          // laser on, no feed rate
        Assert.False(JobAudit.Check("G1F600\nG1X10S500\n", 0).Ok);       // unknown power
        Assert.True(JobAudit.Check("G0X10Y10\nM5\n", W).Ok);            // never fires
    }
}

/// <summary>
/// Corner crosses, number dots and the "Grid N" label are filled like the grid's hottest cell
/// (its line spacing, speed, power and passes), so they mark wherever that cell does.
/// </summary>
public class MarkFillTests
{
    private static readonly SourceProfile Src = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];

    private static double? Word(string line, char letter)
    {
        int i = line.IndexOf(letter, 1);
        if (i < 0) return null;
        int j = i + 1;
        while (j < line.Length && (char.IsDigit(line[j]) || line[j] is '.' or '-')) j++;
        return j > i + 1 ? double.Parse(line[(i + 1)..j], System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    [Theory]
    [InlineData(1000, 1)]
    [InlineData(1000, 2)]
    [InlineData(5000, 3)]
    public void Cross_arms_are_filled_bars_at_the_hottest_cells_spacing_and_passes(int linesPerCm, int passes)
    {
        // The hottest cell a grid may hold: exactly at the planner's limit.
        var hot = new MopaSettings { PowerPercent = 60, LineDensityPerCm = linesPerCm, FrequencyKHz = 300, PulseWidthNs = 6, Passes = passes, SpeedMmPerS = 1 };
        hot = hot with { SpeedMmPerS = Math.Round(60.0 * passes / (HeatLimits.AbsoluteMaxJPerMm2 * hot.LinePitchMm)) + 1 };
        Assert.True(HeatLimits.ArealEnergy(hot, Src, Lens) <= HeatLimits.AbsoluteMaxJPerMm2);
        var cool = hot with { PowerPercent = 30, Passes = 1, LineDensityPerCm = 300 };
        var layout = new GridLayout { Columns = 4, Rows = 2, CellSizeMm = 8, GapMm = 2 };
        var g = new TestGrid
        {
            Title = "t", MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id, Layout = layout, Number = 0,
            Cells = Enumerable.Range(0, 8).Select(i => new GridCell(i / 4, i % 4, GridLayout.Label(i / 4, i % 4), i == 7 ? hot : cool)).ToList(),
        };
        string job = GcodeExport.ToGcode(g, cornerMarks: true);
        string marks = job[job.LastIndexOf("M38F", StringComparison.Ordinal)..];
        Assert.Contains($"G1F{(int)(hot.SpeedMmPerS * 60)}", marks);
        Assert.Contains("M39P6", marks);

        double size = CornerMarks.SizeMm(layout);
        // Powered strokes running the full length of a cross arm: bar width / spacing, per arm, per pass.
        int arms = marks.Split('\n').Count(l => l.StartsWith("G1") && Word(l, 'S') is > 0) ;
        int expected = 3 * 2 * passes * (int)Math.Ceiling((GcodeExport.MarkBarMm - hot.LinePitchMm / 2) / hot.LinePitchMm);
        Assert.Equal(expected, arms); // no number, so no dots or label: only the crosses
        // And the job still passes the heat check.
        Assert.True(JobAudit.Check(job, 100).Ok);
    }

    [Fact]
    public void Number_dots_and_label_are_filled_too()
    {
        var hot = new MopaSettings { PowerPercent = 60, SpeedMmPerS = 1500, LineDensityPerCm = 2000, FrequencyKHz = 300, PulseWidthNs = 6, Passes = 1 };
        var layout = new GridLayout { Columns = 8, Rows = 3, CellSizeMm = 8, GapMm = 2 };
        var g = new TestGrid
        {
            Title = "t", MaterialId = "m", SourceId = Src.Id, LensId = Lens.Id, Layout = layout, Number = 29,
            Cells = Enumerable.Range(0, 24).Select(i => new GridCell(i / 8, i % 8, GridLayout.Label(i / 8, i % 8), hot)).ToList(),
        };
        var plain = GcodeExport.ToGcode(g with { Number = 0 }, cornerMarks: true);
        var numbered = GcodeExport.ToGcode(g, cornerMarks: true);
        int Fired(string j) => j.Split('\n').Count(l => l.StartsWith("G1") && Word(l, 'S') is > 0);
        int extra = Fired(numbered) - Fired(plain);
        // At least a whole filled dot (0.6 mm at 0.005 mm spacing is over 100 lines) per set bit, and the label on top.
        int setBits = CardId.Encode(29).Count(b => b);
        Assert.True(extra > setBits * 100, $"{extra} strokes for {setBits} dots and the label");
        Assert.True(JobAudit.Check(numbered, 100).Ok);
    }
}
