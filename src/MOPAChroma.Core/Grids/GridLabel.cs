namespace MOPAChroma.Grids;

/// <summary>
/// "Grid 29" engraved in the bottom margin of an Atlas grid, for people: on the line of the
/// bottom-left corner cross, starting just right of it and kept clear of the bottom-right
/// corner (which must stay empty, so the reader can tell which way up the card is). The
/// machine-readable number is the dot row at the top (<see cref="CardId"/>); only grids with
/// that dot row get the label, because the reader uses the dots to tell the letters from a cross
/// when a grid's first columns left no mark.
/// </summary>
public static class GridLabel
{
    public static string Text(int number) => $"Grid {number}";

    /// <summary>Width of each engraved letter stroke, mm (filled, so it marks like a cell).</summary>
    public const double StrokeMm = 0.15;

    /// <summary>Smallest letter height worth engraving, mm.</summary>
    public const double MinHeightMm = 1.0;

    /// <summary>
    /// The label's strokes in machine mm, or empty when the grid has no number or no room.
    /// <paramref name="originX"/>, <paramref name="originY"/> are the top-left corner of the first cell.
    /// </summary>
    public static List<List<(double X, double Y)>> StrokesMm(GridLayout l, int number, double originX, double originY)
    {
        // Only beside the number dots: those let the card reader tell the label from a corner cross.
        if (number is < 1 or > CardId.MaxNumber || CardId.For(l) is null) return [];
        string text = Text(number);
        double pitch = l.CellSizeMm + l.GapMm;
        double size = CornerMarks.SizeMm(l), half = size / 2, clear = size / 2;
        double lineY = originY + (l.Rows - 1) * pitch + l.CellSizeMm / 2 + CornerMarks.OffsetMm(l);
        double left = originX + l.CellSizeMm / 2 + half + clear;
        double right = originX + (l.Columns - 1) * pitch + l.CellSizeMm / 2 - half - clear;
        double height = size - StrokeMm; // so the stroke's width stays inside the crosses' band
        double w = StrokeFont.Width(text, height) + StrokeMm;
        if (w > right - left) height *= (right - left - StrokeMm) / (w - StrokeMm);
        if (height < MinHeightMm) return [];
        return StrokeFont.Polylines(text, left + StrokeMm / 2, lineY + height / 2, height);
    }
}
