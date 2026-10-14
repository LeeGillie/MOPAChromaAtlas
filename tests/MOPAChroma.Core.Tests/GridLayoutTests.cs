using System.Globalization;
using System.Xml.Linq;
using MOPAChroma.Grids;
using LumosUltra.Jobs;
namespace MOPAChroma.Tests;

public class GridLayoutTests
{
    private static readonly SourceProfile Source = SourceProfile.Presets[0];
    private static readonly LensProfile Lens = LensProfile.Presets[0];

    [Fact]
    public void Fit_to_coupon_matches_hand_computed_columns_and_rows()
    {
        // 20x10 cells of 4mm, gap 0.5mm is exactly 89.5 x 44.5mm with no margin or label
        // band; FitToCoupon also reserves FrameMarginMm (3mm) on every side, so fewer
        // cells than that fit within the same coupon.
        var (cols, rows) = GridLayout.FitToCoupon(89.5, 44.5, 4, 0.5, includeLabels: false);
        Assert.Equal(18, cols);
        Assert.Equal(8, rows);
    }

    [Fact]
    public void Fit_to_coupon_with_labels_leaves_room_for_the_label_band()
    {
        // Same coupon: width is unaffected by labels, but far fewer rows fit once each
        // reserves a label band above it (and the header band once at the top).
        var (cols, rows) = GridLayout.FitToCoupon(89.5, 44.5, 4, 0.5, includeLabels: true);
        Assert.Equal(18, cols);
        Assert.True(rows < 8, $"expected fewer than 8 rows with labels, got {rows}");
    }

    [Fact]
    public void Fit_to_coupon_never_returns_less_than_one_cell()
    {
        var (cols, rows) = GridLayout.FitToCoupon(couponWidthMm: 3, couponHeightMm: 3, cellSizeMm: 8, gapMm: 2);
        Assert.Equal(1, cols);
        Assert.Equal(1, rows);
    }

    [Fact]
    public void Exported_svg_with_labels_never_exceeds_the_coupon_it_was_fitted_to()
    {
        double couponW = 90, couponH = 45, cell = 4, gap = 0.5;
        var (cols, rows) = GridLayout.FitToCoupon(couponW, couponH, cell, gap);
        var layout = new GridLayout { Columns = cols, Rows = rows, CellSizeMm = cell, GapMm = gap };

        var settings = Enumerable.Range(0, cols * rows).Select(_ => new MopaSettings
        {
            PowerPercent = 50, SpeedMmPerS = 800, FrequencyKHz = 200, PulseWidthNs = 6, LineDensityPerCm = 5000,
        });
        var grid = TestGrid.Arrange("t", "p", layout, settings, Source, Lens, "m");

        var svg = GridExport.ToSvg(grid, includeLabels: true);
        var root = XDocument.Parse(svg).Root!;
        double w = double.Parse(root.Attribute("width")!.Value.Replace("mm", ""), CultureInfo.InvariantCulture);
        double h = double.Parse(root.Attribute("height")!.Value.Replace("mm", ""), CultureInfo.InvariantCulture);

        Assert.True(w <= couponW + 0.01, $"exported width {w} exceeds coupon {couponW}");
        Assert.True(h <= couponH + 0.01, $"exported height {h} exceeds coupon {couponH}");
    }

    [Fact]
    public void Exported_svg_carries_a_reference_header_so_a_scan_can_find_its_grid()
    {
        var layout = new GridLayout { Columns = 3, Rows = 2, CellSizeMm = 8, GapMm = 2 };
        var settings = Enumerable.Range(0, 6).Select(_ => new MopaSettings
        {
            PowerPercent = 50, SpeedMmPerS = 800, FrequencyKHz = 200, PulseWidthNs = 6, LineDensityPerCm = 5000,
        });
        var grid = TestGrid.Arrange("Stainless steel 304 — grid 1", "Seed", layout, settings, Source, Lens, "m");

        var withHeader = GridExport.ToSvg(grid, includeLabels: true);
        Assert.Contains(GridExport.HeaderText(grid), withHeader);

        var withoutHeader = GridExport.ToSvg(grid, includeLabels: false);
        Assert.DoesNotContain("id=\"header\"", withoutHeader);
    }

    [Fact]
    public void Exported_svg_gives_cells_and_lettering_different_colours_so_they_split_into_separate_layers()
    {
        // Cells keep individually-set black fill (each square can get its own settings);
        // the header and per-cell labels share a second colour (one common setting for
        // all the lettering) so an importer that groups by colour treats them separately.
        var layout = new GridLayout { Columns = 3, Rows = 2, CellSizeMm = 8, GapMm = 2 };
        var settings = Enumerable.Range(0, 6).Select(_ => new MopaSettings
        {
            PowerPercent = 50, SpeedMmPerS = 800, FrequencyKHz = 200, PulseWidthNs = 6, LineDensityPerCm = 5000,
        });
        var grid = TestGrid.Arrange("t", "p", layout, settings, Source, Lens, "m");

        var doc = XDocument.Parse(GridExport.ToSvg(grid, includeLabels: true));
        var cellFills = doc.Descendants().Where(e => e.Attribute("id")?.Value is "A1" or "B1")
            .Select(e => e.Attribute("fill")!.Value).ToList();
        var letteringFills = doc.Descendants().Where(e => (string?)e.Attribute("id") == "header")
            .Select(e => e.Attribute("fill")!.Value).ToList();

        Assert.All(cellFills, f => Assert.Equal("#000000", f));
        Assert.All(letteringFills, f => Assert.NotEqual("#000000", f));
        Assert.NotEqual(cellFills[0], letteringFills[0]);
    }

    [Fact]
    public void Exported_svg_nests_content_and_frame_under_one_coupon_group()
    {
        // The whole card - frame plus engraved content - should be draggable as a unit.
        var layout = new GridLayout { Columns = 3, Rows = 2, CellSizeMm = 8, GapMm = 2 };
        var settings = Enumerable.Range(0, 6).Select(_ => new MopaSettings
        {
            PowerPercent = 50, SpeedMmPerS = 800, FrequencyKHz = 200, PulseWidthNs = 6, LineDensityPerCm = 5000,
        });
        var grid = TestGrid.Arrange("t", "p", layout, settings, Source, Lens, "m");

        var doc = XDocument.Parse(GridExport.ToSvg(grid, couponWidthMm: 60, couponHeightMm: 40));
        var coupon = doc.Descendants().Single(e => (string?)e.Attribute("id") == "coupon");
        Assert.Contains(coupon.Descendants(), e => (string?)e.Attribute("id") == "content");
        Assert.Contains(coupon.Descendants(), e => (string?)e.Attribute("id") == "frame");
    }

    [Fact]
    public void Label_band_scales_down_for_small_cells_instead_of_a_fixed_size()
    {
        // A 3mm cell (typical once a coupon is fit tightly) should reserve a noticeably
        // smaller band than a large cell - not the old fixed 4mm regardless of cell size.
        double smallBand = GridLayout.LabelBandMm(3);
        double largeBand = GridLayout.LabelBandMm(20);
        Assert.True(smallBand < 4.0, $"expected a small-cell band under 4mm, got {smallBand}");
        Assert.True(largeBand > smallBand, "a bigger cell should get a bigger label band");
    }

    [Fact]
    public void Exported_svg_frame_matches_the_true_coupon_size_and_corner_radius()
    {
        double couponW = 90, couponH = 45, cell = 4, gap = 0.5, radius = 2.5;
        var (cols, rows) = GridLayout.FitToCoupon(couponW, couponH, cell, gap);
        var layout = new GridLayout { Columns = cols, Rows = rows, CellSizeMm = cell, GapMm = gap };
        var settings = Enumerable.Range(0, cols * rows).Select(_ => new MopaSettings
        {
            PowerPercent = 50, SpeedMmPerS = 800, FrequencyKHz = 200, PulseWidthNs = 6, LineDensityPerCm = 5000,
        });
        var grid = TestGrid.Arrange("t", "p", layout, settings, Source, Lens, "m");

        var svg = GridExport.ToSvg(grid, couponWidthMm: couponW, couponHeightMm: couponH, cornerRadiusMm: radius);
        var doc = XDocument.Parse(svg);
        var root = doc.Root!;

        // The overall canvas grows to the true coupon size, not just the (usually smaller) cell content.
        double w = double.Parse(root.Attribute("width")!.Value.Replace("mm", ""), CultureInfo.InvariantCulture);
        double h = double.Parse(root.Attribute("height")!.Value.Replace("mm", ""), CultureInfo.InvariantCulture);
        Assert.Equal(couponW, w, 2);
        Assert.Equal(couponH, h, 2);

        // A nonzero radius is drawn as an explicit arc path, not rect rx/ry (MakeIt's SVG
        // import was found to ignore rx/ry - corners came out square).
        var frame = doc.Descendants().Single(e => (string?)e.Attribute("id") == "frame");
        Assert.Equal("path", frame.Name.LocalName);
        string d = frame.Attribute("d")!.Value;
        Assert.Contains($"M{radius},0", d);                          // starts inset by the radius
        Assert.Contains($"A{radius},{radius}", d);                   // corner arcs use the given radius
        Assert.Contains($"{couponW - radius},0", d);                 // reaches the far corners
        Assert.Contains($"{couponH - radius}", d);
        Assert.Equal("none", frame.Attribute("fill")!.Value);

        // No coupon size given: no frame, and the canvas stays tight to the cell content.
        var noFrame = GridExport.ToSvg(grid);
        Assert.DoesNotContain("id=\"frame\"", noFrame);
    }

    [Fact]
    public void Exported_svg_insets_content_from_the_frame_by_the_margin()
    {
        // Content used to sit flush against the frame's left/top edge; it should now be
        // offset inward by FrameMarginMm on every side.
        var layout = new GridLayout { Columns = 3, Rows = 2, CellSizeMm = 8, GapMm = 2 };
        var settings = Enumerable.Range(0, 6).Select(_ => new MopaSettings
        {
            PowerPercent = 50, SpeedMmPerS = 800, FrequencyKHz = 200, PulseWidthNs = 6, LineDensityPerCm = 5000,
        });
        var grid = TestGrid.Arrange("t", "p", layout, settings, Source, Lens, "m");

        var svg = GridExport.ToSvg(grid, couponWidthMm: 60, couponHeightMm: 40);
        var content = XDocument.Parse(svg).Descendants().Single(e => (string?)e.Attribute("id") == "content");
        Assert.Equal($"translate({GridLayout.FrameMarginMm},{GridLayout.FrameMarginMm})", content.Attribute("transform")!.Value);

        // No coupon given: no known physical edge, so no margin is applied.
        var noCoupon = GridExport.ToSvg(grid);
        var noCouponContent = XDocument.Parse(noCoupon).Descendants().Single(e => (string?)e.Attribute("id") == "content");
        Assert.Equal("translate(0,0)", noCouponContent.Attribute("transform")!.Value);
    }

    [Fact]
    public void Exported_svg_frame_has_no_radius_attribute_when_corners_are_square()
    {
        var layout = new GridLayout { Columns = 3, Rows = 2, CellSizeMm = 8, GapMm = 2 };
        var settings = Enumerable.Range(0, 6).Select(_ => new MopaSettings
        {
            PowerPercent = 50, SpeedMmPerS = 800, FrequencyKHz = 200, PulseWidthNs = 6, LineDensityPerCm = 5000,
        });
        var grid = TestGrid.Arrange("t", "p", layout, settings, Source, Lens, "m");

        var svg = GridExport.ToSvg(grid, couponWidthMm: 60, couponHeightMm: 40, cornerRadiusMm: 0);
        var frame = XDocument.Parse(svg).Descendants().Single(e => (string?)e.Attribute("id") == "frame");
        Assert.Null(frame.Attribute("rx"));
    }
}
