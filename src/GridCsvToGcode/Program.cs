using System.Globalization;
using MOPAChroma.Grids;
using LumosUltra.Jobs;
if (args.Length < 6)
{
    Console.Error.WriteLine("Usage: GridCsvToGcode <settings.csv> <columns> <rows> <cellSizeMm> <gapMm> <output.gc>");
    return 1;
}

string csvPath = args[0];
int columns = int.Parse(args[1], CultureInfo.InvariantCulture);
int rows = int.Parse(args[2], CultureInfo.InvariantCulture);
double cellSizeMm = double.Parse(args[3], CultureInfo.InvariantCulture);
double gapMm = double.Parse(args[4], CultureInfo.InvariantCulture);
string outputPath = args[5];

var rowsOfCsv = File.ReadAllLines(csvPath).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
if (rowsOfCsv.Count != columns * rows)
    throw new InvalidOperationException($"CSV has {rowsOfCsv.Count} data rows but columns*rows = {columns * rows}.");

// Built directly (not via TestGrid.Arrange, which re-sorts cells by energy) so each
// cell keeps exactly the row/column position implied by the CSV's own row order -
// the same order GridExport.ToCsv wrote it in.
var cells = new List<GridCell>(rowsOfCsv.Count);
for (int i = 0; i < rowsOfCsv.Count; i++)
{
    var f = rowsOfCsv[i].Split(',');
    var settings = new MopaSettings
    {
        PowerPercent = double.Parse(f[1], CultureInfo.InvariantCulture),
        SpeedMmPerS = double.Parse(f[2], CultureInfo.InvariantCulture),
        LineDensityPerCm = double.Parse(f[3], CultureInfo.InvariantCulture),
        FrequencyKHz = double.Parse(f[4], CultureInfo.InvariantCulture),
        PulseWidthNs = double.Parse(f[5], CultureInfo.InvariantCulture),
        Passes = int.Parse(f[6], CultureInfo.InvariantCulture),
    };
    cells.Add(new GridCell(i / columns, i % columns, f[0], settings));
}

var layout = new GridLayout { Columns = columns, Rows = rows, CellSizeMm = cellSizeMm, GapMm = gapMm };
var grid = new TestGrid
{
    Title = Path.GetFileNameWithoutExtension(csvPath),
    MaterialId = "csv-import",
    SourceId = SourceProfile.Presets[0].Id,
    LensId = LensProfile.Presets[0].Id,
    Layout = layout,
    Cells = cells,
};

var gcode = GcodeExport.ToGcode(grid);
File.WriteAllText(outputPath, gcode);
Console.WriteLine($"Wrote {outputPath}: {gcode.Length:N0} bytes, {cells.Count} cells.");
return 0;
