using System.Globalization;
using System.Text;
using System.Xml;
using MOPAChroma.Laser;

namespace MOPAChroma.Grids;

/// <summary>
/// Writes a planned grid out for engraving.
///
/// SVG: three groups, each its own colour so MakeIt and LightBurn split them into
/// separate layers by colour: the cell squares (black fill - each keeps a distinct id so
/// every one can be given its own settings from the CSV), the header and per-cell labels
/// (blue fill - one common marking setting for all of them), and a non-filled frame at
/// the coupon's true stock size for physical alignment (magenta stroke - give that layer
/// its own settings and set it to skip/ignore; it is a framing aid, never meant to be
/// engraved). The labels and cells nest under one "content" group, which nests with the
/// frame under one outer "coupon" group, so the whole card can be selected and dragged
/// as a unit. Import into MakeIt or LightBurn and assign each cell its settings from
/// the CSV.
///
/// CSV: the settings sheet, one row per cell, in MakeIt's own units.
/// </summary>
public static class GridExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string Ns = "http://www.w3.org/2000/svg";

    /// <summary>Stroke colour for the non-engraved framing rectangle - distinct from the
    /// engraved content so it is easy to pick out as its own colour/layer.</summary>
    private const string FrameColor = "#FF00FF";

    /// <summary>Fill colour for the header and per-cell labels - distinct from the cell
    /// squares' black, so MakeIt/LightBurn split "one common setting for all the
    /// lettering" from "each square gets its own setting" into separate layers.</summary>
    private const string LetteringColor = "#0000FF";

    /// <summary>The reference text engraved centred across the top of the coupon, so a
    /// scan of the physical card can be matched back to the grid that planned it.</summary>
    public static string HeaderText(TestGrid grid) =>
        $"{grid.Card?.CardId ?? grid.Title}  ·  {grid.CreatedUtc.LocalDateTime:yyyy-MM-dd}";

    /// <param name="grid">The planned grid to render.</param>
    /// <param name="includeLabels">Whether to engrave the header and per-cell labels.</param>
    /// <param name="couponWidthMm">The physical coupon's true width, if known. When given
    /// (with <paramref name="couponHeightMm"/>), a non-filled reference rectangle is drawn
    /// at this size - the cell grid is usually smaller, since <see cref="GridLayout.FitToCoupon"/>
    /// rounds down to a whole number of cells.</param>
    /// <param name="couponHeightMm">The physical coupon's true height, if known.</param>
    /// <param name="cornerRadiusMm">Corner radius of the reference rectangle, to match the
    /// coupon stock's own rounded corners. Ignored unless the coupon size is given.</param>
    public static string ToSvg(TestGrid grid, bool includeLabels = true,
        double? couponWidthMm = null, double? couponHeightMm = null, double cornerRadiusMm = 0)
    {
        var l = grid.Layout;
        // With a known coupon, content is inset from the frame so nothing sits flush
        // against the physical edge; without one there is no known edge to avoid.
        bool hasFrame = couponWidthMm is not null && couponHeightMm is not null;
        double margin = hasFrame ? GridLayout.FrameMarginMm : 0.0;
        double labelBand = includeLabels ? GridLayout.LabelBandMm(l.CellSizeMm) : 0.0;
        double headerBand = includeLabels ? GridLayout.HeaderBandMm : 0.0;
        double contentW = l.WidthMm, contentH = headerBand + l.HeightMm + labelBand * l.Rows;
        double w = Math.Max(contentW + 2 * margin, couponWidthMm ?? 0);
        double h = Math.Max(contentH + 2 * margin, couponHeightMm ?? 0);
        double pitchY = l.CellSizeMm + l.GapMm + labelBand;

        var sb = new StringBuilder();
        // A StringBuilder target would make XmlWriter declare utf-16, so no declaration.
        var xs = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true };
        using (var x = XmlWriter.Create(sb, xs))
        {
            x.WriteStartElement("svg", Ns);
            x.WriteAttributeString("width", F(w) + "mm");
            x.WriteAttributeString("height", F(h) + "mm");
            x.WriteAttributeString("viewBox", $"0 0 {F(w)} {F(h)}");

            x.WriteElementString("title", Ns, grid.Title);
            x.WriteComment($" MOPAChroma Atlas grid {grid.Id} | {grid.Purpose} | material {grid.MaterialId} | source {grid.SourceId} | lens {grid.LensId} ");

            // Everything - the frame and the engraved content - nests under one group so
            // the whole card can be selected and dragged as a unit.
            x.WriteStartElement("g", Ns);
            x.WriteAttributeString("id", "coupon");

            // Cells and lettering are positioned relative to the content origin; this
            // single translate is what applies the frame margin to both at once.
            x.WriteStartElement("g", Ns);
            x.WriteAttributeString("id", "content");
            x.WriteAttributeString("transform", $"translate({F(margin)},{F(margin)})");

            x.WriteStartElement("g", Ns);
            x.WriteAttributeString("id", "cells");
            foreach (var c in grid.Cells)
            {
                x.WriteStartElement("rect", Ns);
                x.WriteAttributeString("id", c.Label);
                x.WriteAttributeString("x", F(c.Column * (l.CellSizeMm + l.GapMm)));
                x.WriteAttributeString("y", F(headerBand + c.Row * pitchY + labelBand));
                x.WriteAttributeString("width", F(l.CellSizeMm));
                x.WriteAttributeString("height", F(l.CellSizeMm));
                x.WriteAttributeString("fill", "#000000");
                x.WriteEndElement();
            }
            x.WriteEndElement(); // cells

            if (includeLabels)
            {
                // The header and per-cell labels are grouped together as "lettering":
                // one shared colour so the importer treats them as one layer with one
                // common marking setting, distinct from the individually-set cell squares.
                x.WriteStartElement("g", Ns);
                x.WriteAttributeString("id", "lettering");

                string header = HeaderText(grid);
                double contentAreaW = w - 2 * margin;
                // Cap font size so the header never overhangs the coupon width, with the
                // same margin as the cell labels so it doesn't run edge to edge either.
                double headerFont = Math.Min(3.2, contentAreaW * 0.8 / (Math.Max(1, header.Length) * 0.6));
                x.WriteStartElement("text", Ns);
                x.WriteAttributeString("id", "header");
                x.WriteAttributeString("x", F(contentAreaW / 2));
                x.WriteAttributeString("y", F(headerBand - 1.2));
                x.WriteAttributeString("font-family", "Arial, Helvetica, sans-serif");
                x.WriteAttributeString("font-size", F(headerFont, "0.##"));
                x.WriteAttributeString("text-anchor", "middle");
                x.WriteAttributeString("fill", LetteringColor);
                x.WriteString(header);
                x.WriteEndElement();

                foreach (var c in grid.Cells)
                {
                    double fontSize = GridLayout.LabelFontSizeMm(l.CellSizeMm, c.Label.Length);
                    x.WriteStartElement("text", Ns);
                    x.WriteAttributeString("x", F(c.Column * (l.CellSizeMm + l.GapMm) + l.CellSizeMm / 2));
                    x.WriteAttributeString("y", F(headerBand + c.Row * pitchY + labelBand - 0.8));
                    x.WriteAttributeString("font-family", "Arial, Helvetica, sans-serif");
                    x.WriteAttributeString("font-size", F(fontSize, "0.##"));
                    x.WriteAttributeString("text-anchor", "middle");
                    x.WriteAttributeString("fill", LetteringColor);
                    x.WriteString(c.Label);
                    x.WriteEndElement();
                }
                x.WriteEndElement(); // lettering
            }

            x.WriteEndElement(); // content

            if (couponWidthMm is { } cw && couponHeightMm is { } ch)
            {
                double r = Math.Clamp(cornerRadiusMm, 0, Math.Min(cw, ch) / 2);
                if (r > 0)
                {
                    // MakeIt's SVG import was found to ignore rect rx/ry (corners came out
                    // square), so rounded corners are drawn as an explicit arc path instead.
                    x.WriteStartElement("path", Ns);
                    x.WriteAttributeString("id", "frame");
                    x.WriteAttributeString("d", RoundedRectPath(0, 0, cw, ch, r));
                }
                else
                {
                    x.WriteStartElement("rect", Ns);
                    x.WriteAttributeString("id", "frame");
                    x.WriteAttributeString("x", "0");
                    x.WriteAttributeString("y", "0");
                    x.WriteAttributeString("width", F(cw));
                    x.WriteAttributeString("height", F(ch));
                }
                x.WriteAttributeString("fill", "none");
                x.WriteAttributeString("stroke", FrameColor);
                x.WriteAttributeString("stroke-width", "0.1");
                x.WriteEndElement();
            }

            x.WriteEndElement(); // coupon

            x.WriteEndElement(); // svg
        }
        return sb.ToString();
    }

    /// <summary>An SVG path 'd' for a rounded rectangle, as explicit line and arc
    /// commands rather than a &lt;rect&gt; with rx/ry (MakeIt's SVG import was found to
    /// ignore rx/ry - corners came out square).</summary>
    private static string RoundedRectPath(double x, double y, double width, double height, double r)
    {
        string P(double v1, double v2) => $"{F(v1)},{F(v2)}";
        return string.Join(" ",
            $"M{P(x + r, y)}",
            $"L{P(x + width - r, y)}",
            $"A{P(r, r)} 0 0 1 {P(x + width, y + r)}",
            $"L{P(x + width, y + height - r)}",
            $"A{P(r, r)} 0 0 1 {P(x + width - r, y + height)}",
            $"L{P(x + r, y + height)}",
            $"A{P(r, r)} 0 0 1 {P(x, y + height - r)}",
            $"L{P(x, y + r)}",
            $"A{P(r, r)} 0 0 1 {P(x + r, y)}",
            "Z");
    }

    public static string ToCsv(TestGrid grid, SourceProfile source, LensProfile lens)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Cell,Power %,Speed mm/s,Line density /cm,Frequency kHz,Pulse width ns,Passes," +
                      "Pulse energy mJ,Peak power kW,Pulse overlap %,Line overlap %,Areal energy J/mm2");
        foreach (var c in grid.Cells)
        {
            var s = c.Settings;
            var p = Physics.From(s, source, lens);
            sb.AppendLine(string.Join(",",
                c.Label, F(s.PowerPercent), F(s.SpeedMmPerS), F(s.LineDensityPerCm), F(s.FrequencyKHz),
                F(s.PulseWidthNs), s.Passes.ToString(Inv),
                F(p.PulseEnergyMJ, "0.####"), F(p.PeakPowerKW, "0.###"), F(p.PulseOverlap * 100, "0.#"),
                F(p.LineOverlap * 100, "0.#"), F(p.ArealEnergyJPerMm2, "0.####")));
        }
        return sb.ToString();
    }

    private static string F(double v, string fmt = "0.###") => v.ToString(fmt, Inv);
}
