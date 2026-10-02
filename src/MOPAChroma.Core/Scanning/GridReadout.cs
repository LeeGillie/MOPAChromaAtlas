using MOPAChroma.Atlas;
using MOPAChroma.Grids;

namespace MOPAChroma.Scanning;

/// <summary>
/// Turns a reading of an Atlas grid's card (scan or photo) into one sample per planned
/// cell, keeping each cell's own settings and label so the result lands on the right
/// recipe whatever order the cells were engraved in.
/// </summary>
public static class GridReadout
{
    /// <param name="grid">The planned grid that was engraved.</param>
    /// <param name="reading">Every cell measured from the image, by row and column.</param>
    /// <param name="outcome">The confirmed result per cell (row, column): usually the suggestion, as corrected by the user.</param>
    /// <param name="capture">The image the reading came from: decides method and viewing condition.</param>
    /// <param name="turnDegrees">How the card was turned in the image.</param>
    /// <param name="perspective">Whether the image was taken at an angle.</param>
    /// <param name="notes">Where the reading came from, for the sample's notes.</param>
    public static IReadOnlyList<Sample> ToSamples(TestGrid grid, ScanMeasurement reading, Func<int, int, Outcome> outcome,
                                                  CardCapture capture, int turnDegrees, bool perspective, string notes)
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<Sample>(grid.Cells.Count);
        foreach (var cell in grid.Cells)
        {
            var r = reading.At(cell.Row, cell.Column);
            var o = outcome(cell.Row, cell.Column);
            bool coloured = o is Outcome.Good or Outcome.Uneven;
            list.Add(new Sample
            {
                MaterialId = grid.MaterialId, SourceId = grid.SourceId, LensId = grid.LensId,
                GridId = grid.Id, CellLabel = cell.Label, Settings = cell.Settings,
                Outcome = o,
                Color = coloured ? r.Lab : null,
                Method = coloured ? capture.Method : MeasureMethod.None,
                ViewingCondition = capture.ViewingCondition(turnDegrees, perspective),
                MeasuredUtc = now,
                Notes = $"{notes}; spread {r.Spread:0.0}; ΔE00 from bare {r.DeltaEFromBare:0.0}",
            });
        }
        return list;
    }
}
