using MOPAChroma.Laser;

namespace MOPAChroma.Grids;

/// <summary>Physical layout of a test coupon.</summary>
public sealed record GridLayout
{
    public int Columns { get; init; } = 6;
    public int Rows { get; init; } = 4;
    public double CellSizeMm { get; init; } = 8;
    public double GapMm { get; init; } = 2;

    public int Capacity => Columns * Rows;
    public double WidthMm => Columns * CellSizeMm + (Columns - 1) * GapMm;
    public double HeightMm => Rows * CellSizeMm + (Rows - 1) * GapMm;

    /// <summary>Vertical space GridExport reserves once, across the full width at the top
    /// of the coupon, for the card's reference header (id and date).</summary>
    public const double HeaderBandMm = 6.0;

    /// <summary>Gap GridExport leaves between the coupon's physical edge (the reference
    /// frame) and the nearest engraved content, on every side, so nothing sits flush
    /// against the edge of the stock.</summary>
    public const double FrameMarginMm = 3.0;

    // The label-sizing constants below are shared between GridExport (which renders text)
    // and FitToCoupon (which has to know the space that text will need before any text
    // exists to measure). Keeping them here means both always agree.
    private const double LabelMaxFontMm = 2.8;
    private const double LabelWidthMarginMm = 0.8;   // target 80% of cell width, so labels don't touch
    private const double LabelCharWidthRatio = 0.6;  // rough average glyph width as a fraction of font size

    /// <summary>The font size GridExport uses for one cell's label, capped so it never
    /// overhangs the cell whatever the label's length.</summary>
    public static double LabelFontSizeMm(double cellSizeMm, int labelLength) =>
        Math.Min(LabelMaxFontMm, cellSizeMm * LabelWidthMarginMm / (Math.Max(1, labelLength) * LabelCharWidthRatio));

    /// <summary>Vertical space GridExport reserves above each row for that row's cell
    /// labels, sized to the font the labels will actually use (so small cells don't waste
    /// height on an oversized fixed band). Assumes 2-character labels ("A1"), true up to
    /// 26 columns and 9 rows; a longer label just shrinks further inside the same band.</summary>
    public static double LabelBandMm(double cellSizeMm) => LabelFontSizeMm(cellSizeMm, 2) + 1.2;

    /// <summary>Spreadsheet-style label: A1 is top-left, letters are columns, numbers rows.</summary>
    public static string Label(int row, int col) => $"{ColumnLetters(col)}{row + 1}";

    /// <summary>
    /// The largest grid of <paramref name="cellSizeMm"/> cells, <paramref name="gapMm"/> apart,
    /// that fits inside a coupon <paramref name="couponWidthMm"/> by <paramref name="couponHeightMm"/>.
    /// When <paramref name="includeLabels"/>, each row also reserves <see cref="LabelBandMm"/>
    /// above it and the coupon reserves <see cref="HeaderBandMm"/> once at the top, matching
    /// what GridExport actually writes onto the coupon, minus <see cref="FrameMarginMm"/> on
    /// every side so nothing sits flush against the coupon's physical edge. Always at least 1x1.
    /// </summary>
    public static (int Columns, int Rows) FitToCoupon(
        double couponWidthMm, double couponHeightMm, double cellSizeMm, double gapMm, bool includeLabels = true)
    {
        double usableWidthMm = couponWidthMm - 2 * FrameMarginMm;
        double usableHeightMm = couponHeightMm - 2 * FrameMarginMm - (includeLabels ? HeaderBandMm : 0);
        double rowPitch = cellSizeMm + gapMm + (includeLabels ? LabelBandMm(cellSizeMm) : 0);
        int cols = Math.Clamp((int)Math.Floor((usableWidthMm + gapMm) / (cellSizeMm + gapMm)), 1, 200);
        int rows = Math.Clamp((int)Math.Floor((usableHeightMm + gapMm) / rowPitch), 1, 200);
        return (cols, rows);
    }

    private static string ColumnLetters(int col)
    {
        var s = "";
        col++;
        while (col > 0)
        {
            int m = (col - 1) % 26;
            s = (char)('A' + m) + s;
            col = (col - 1) / 26;
        }
        return s;
    }
}

/// <summary>One cell of a grid. <c>Reason</c> is why the planner chose it, shown to the user.</summary>
public sealed record GridCell(int Row, int Column, string Label, MopaSettings Settings, string Reason = "");

/// <summary>
/// A planned test engraving. Its cells are the settings the planner chose; nothing
/// about the grid implies the settings vary along rows or columns, because the
/// point is to NOT sweep knobs one at a time. Cells are placed in order of increasing
/// energy so the coupon reads from faint to strong.
/// </summary>
public sealed record TestGrid
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>
    /// The grid's number in its atlas (1, 2, 3…), never reused: engraved on the card as its ID
    /// code (<see cref="Grids.CardId"/>) so any later picture of the card says which grid it is.
    /// 0 until the grid is added to an atlas.
    /// </summary>
    public int Number { get; init; }
    public required string Title { get; init; }
    public required string MaterialId { get; init; }
    public required string SourceId { get; init; }
    public required string LensId { get; init; }
    public required GridLayout Layout { get; init; }
    public required IReadOnlyList<GridCell> Cells { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>"Seed" for the first exploration of a material, "Refine #n" after that.</summary>
    public string Purpose { get; init; } = "";

    /// <summary>The physical card's record (ID, Z, lot...), for cards actually engraved.</summary>
    public CardInfo? Card { get; init; }

    /// <summary>For cards made with a MakeIt built-in test: how the card was set up.</summary>
    public ColorTestDefinition? Definition { get; init; }

    /// <summary>
    /// This grid with its layout cut down to the rows and columns its cells use. For a grid
    /// planned before grids were shaped to their cells; only for one not yet engraved, because an
    /// engraved card's corner crosses sit where its original layout put them.
    /// </summary>
    public TestGrid Trimmed()
    {
        if (Cells.Count == 0) return this;
        int rows = Cells.Max(c => c.Row) + 1, cols = Math.Max(2, Cells.Max(c => c.Column) + 1);
        return rows == Layout.Rows && cols == Layout.Columns ? this
            : this with { Layout = Layout with { Rows = Math.Min(rows, Layout.Rows), Columns = Math.Min(cols, Layout.Columns) } };
    }

    public static TestGrid Arrange(
        string title, string purpose, GridLayout layout, IEnumerable<MopaSettings> settings,
        SourceProfile source, LensProfile lens, string materialId) =>
        Arrange(title, purpose, layout, settings.Select(s => (s, "")), source, lens, materialId);

    public static TestGrid Arrange(
        string title, string purpose, GridLayout layout, IEnumerable<(MopaSettings Settings, string Reason)> picks,
        SourceProfile source, LensProfile lens, string materialId)
    {
        var ordered = picks
            .OrderBy(p => Physics.From(p.Settings, source, lens).ArealEnergyJPerMm2)
            .Take(layout.Capacity)
            .ToList();

        // Fewer cells than the coupon holds (a micro-grid, say): shape the grid to its cells -
        // as few rows as the width allows, then as few columns as fill them evenly - so the
        // engraving, its corner crosses and the preview are no bigger than the cells.
        if (ordered.Count > 0 && ordered.Count < layout.Capacity)
        {
            int rows = (ordered.Count + layout.Columns - 1) / layout.Columns;
            int cols = (ordered.Count + rows - 1) / rows;
            layout = layout with { Rows = rows, Columns = Math.Max(cols, Math.Min(2, layout.Columns)) };
        }

        var cells = new List<GridCell>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            int r = i / layout.Columns, c = i % layout.Columns;
            cells.Add(new GridCell(r, c, GridLayout.Label(r, c), ordered[i].Settings, ordered[i].Reason));
        }

        return new TestGrid
        {
            Title = title,
            Purpose = purpose,
            MaterialId = materialId,
            SourceId = source.Id,
            LensId = lens.Id,
            Layout = layout,
            Cells = cells,
        };
    }
}
