using System.Globalization;
using MOPAChroma.Grids;
using LumosUltra.Jobs;
namespace MOPAChroma.Tests;

public class GcodeExportTests
{
    private static readonly SourceProfile Source = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];

    // Every formula asserted here comes from docs/CALIBRATION-FINDINGS.md - a
    // real-hardware calibration run cross-checked against the physical engraved part.
    // Do not "simplify" these without re-confirming against real hardware first.

    private static TestGrid OneCellGrid(MopaSettings settings, GridLayout? layout = null)
    {
        layout ??= new GridLayout { Columns = 1, Rows = 1, CellSizeMm = 3, GapMm = 0.5 };
        return TestGrid.Arrange("t", "p", layout, [settings], Source, Lens, "m");
    }

    [Fact]
    public void Power_percent_becomes_S_value_times_ten_on_the_burn_stroke()
    {
        var settings = new MopaSettings { PowerPercent = 60, SpeedMmPerS = 800, LineDensityPerCm = 5, FrequencyKHz = 60, PulseWidthNs = 6 };
        var gcode = GcodeExport.ToGcode(OneCellGrid(settings));

        Assert.Contains("S600\n", gcode);
        Assert.DoesNotContain("S599", gcode);
        Assert.DoesNotContain("S601", gcode);
    }

    [Fact]
    public void Frequency_khz_maps_directly_onto_M38F()
    {
        var settings = new MopaSettings { PowerPercent = 50, SpeedMmPerS = 800, LineDensityPerCm = 5, FrequencyKHz = 90, PulseWidthNs = 6 };
        var gcode = GcodeExport.ToGcode(OneCellGrid(settings));

        Assert.Contains("M38F90\n", gcode);
    }

    [Fact]
    public void Pulse_width_ns_maps_directly_onto_M39P()
    {
        var settings = new MopaSettings { PowerPercent = 50, SpeedMmPerS = 800, LineDensityPerCm = 5, FrequencyKHz = 60, PulseWidthNs = 13 };
        var gcode = GcodeExport.ToGcode(OneCellGrid(settings));

        Assert.Contains("M39P13\n", gcode);
    }

    [Fact]
    public void Speed_mm_per_s_becomes_feed_rate_times_sixty()
    {
        var settings = new MopaSettings { PowerPercent = 50, SpeedMmPerS = 800, LineDensityPerCm = 5, FrequencyKHz = 60, PulseWidthNs = 6 };
        var gcode = GcodeExport.ToGcode(OneCellGrid(settings));

        Assert.Contains("G1F48000\n", gcode); // 800 * 60, confirmed against the physical part's "Speed: 800" label
    }

    [Fact]
    public void Line_density_per_cm_sets_the_scan_line_pitch()
    {
        // 5000 lines/cm -> 0.002mm pitch, confirmed against a real capture's Y increments.
        var dense = new MopaSettings { PowerPercent = 50, SpeedMmPerS = 800, LineDensityPerCm = 5000, FrequencyKHz = 60, PulseWidthNs = 6 };
        var sparse = new MopaSettings { PowerPercent = 50, SpeedMmPerS = 800, LineDensityPerCm = 500, FrequencyKHz = 60, PulseWidthNs = 6 };

        int denseLines = CountBurnLines(GcodeExport.ToGcode(OneCellGrid(dense)));
        int sparseLines = CountBurnLines(GcodeExport.ToGcode(OneCellGrid(sparse)));

        // A 3mm cell at 0.002mm pitch is ~1500 lines; at 0.02mm pitch, ~150 - roughly a
        // 10x difference, matching the 10x difference in line density.
        Assert.True(denseLines > sparseLines * 5, $"expected far more lines at higher density: dense={denseLines} sparse={sparseLines}");
    }

    [Fact]
    public void Multiple_cells_each_carry_their_own_settings_and_position()
    {
        var layout = new GridLayout { Columns = 2, Rows = 1, CellSizeMm = 3, GapMm = 0.5 };
        var settings = new[]
        {
            new MopaSettings { PowerPercent = 30, SpeedMmPerS = 800, LineDensityPerCm = 5, FrequencyKHz = 30, PulseWidthNs = 6 },
            new MopaSettings { PowerPercent = 90, SpeedMmPerS = 800, LineDensityPerCm = 5, FrequencyKHz = 90, PulseWidthNs = 6 },
        };
        var grid = TestGrid.Arrange("t", "p", layout, settings, Source, Lens, "m");

        var gcode = GcodeExport.ToGcode(grid);

        Assert.Contains("S300\n", gcode);
        Assert.Contains("S900\n", gcode);
        Assert.Contains("M38F30\n", gcode);
        Assert.Contains("M38F90\n", gcode);
    }

    [Fact]
    public void Passes_greater_than_one_repeats_the_full_raster()
    {
        var once = new MopaSettings { PowerPercent = 50, SpeedMmPerS = 800, LineDensityPerCm = 100, FrequencyKHz = 60, PulseWidthNs = 6, Passes = 1 };
        var twice = once with { Passes = 2 };

        int linesOnce = CountBurnLines(GcodeExport.ToGcode(OneCellGrid(once)));
        int linesTwice = CountBurnLines(GcodeExport.ToGcode(OneCellGrid(twice)));

        Assert.Equal(linesOnce * 2, linesTwice);
    }

    [Fact]
    public void Output_starts_with_the_confirmed_real_preamble_and_ends_with_the_confirmed_postamble()
    {
        // Copied verbatim from a real MakeIt capture that was confirmed to run
        // correctly (including firing the laser) - see samples/gcode/minimal-test-segment.gc.
        var settings = new MopaSettings { PowerPercent = 50, SpeedMmPerS = 800, LineDensityPerCm = 5, FrequencyKHz = 60, PulseWidthNs = 6 };
        var gcode = GcodeExport.ToGcode(OneCellGrid(settings));

        Assert.StartsWith(";wecreat 3.0.6\n;canvas border: 0 0 210 210\nM15S1\n", gcode);
        Assert.EndsWith("G0X105Y105\nM9\nG1 S0\nM5\nG90\nM6\n", gcode);
    }

    [Fact]
    public void A_cell_with_no_line_density_produces_no_burn_lines_instead_of_dividing_by_zero()
    {
        var settings = new MopaSettings { PowerPercent = 50, SpeedMmPerS = 800, LineDensityPerCm = 0, FrequencyKHz = 60, PulseWidthNs = 6 };
        var gcode = GcodeExport.ToGcode(OneCellGrid(settings));

        Assert.Equal(0, CountBurnLines(gcode));
    }

    private static int CountBurnLines(string gcode) =>
        gcode.Split('\n').Count(l => l.StartsWith("G1X", StringComparison.Ordinal) && !l.Contains("S0", StringComparison.Ordinal));
}
