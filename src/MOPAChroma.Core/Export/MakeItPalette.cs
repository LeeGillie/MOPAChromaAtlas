using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MOPAChroma.Atlas;
using LumosUltra.Jobs;
namespace MOPAChroma.Export;

/// <summary>
/// Writes atlas colours as a WeCreat MakeIt project (.wws): one filled square per colour, in
/// that colour, each carrying its own recipe (power, speed, passes, frequency, pulse width,
/// line density, angle) as a Fill engrave. Open it in MakeIt, then copy a square onto the
/// artwork, or select the artwork and the square and use the square's settings.
/// <para>
/// MakeIt 3.0.6 saves projects encrypted and Atlas never touches that format. It still opens
/// the plain JSON projects that MakeIt 3.0.5 and earlier wrote (checked on Lee's PC,
/// 2026-10-01), so this writes that format. The layout follows a project saved by MakeIt
/// 3.0.5 on Lee's PC (the processList fields, including frequency, pulseWidth and the
/// misspelled lineDesity) and wws-generator's known-good rectangle (Fabric.js 5.3 objects,
/// millimetres on the canvas). Once MakeIt saves the project again it is in MakeIt's own format.
/// </para>
/// <para>
/// Units, from the 3.0.5 project and MakeIt's G-code: power %, speed mm/s, lineDesity lines
/// per cm (with dpi = lines per inch), frequency kHz, pulseWidth ns, repeat = passes.
/// Checked 2026-10-01 in MakeIt 3.0.6 with the Lumos Ultra's MOPA module selected: power,
/// speed, passes, pulse width, frequency and line density all show as written on the Fill
/// tab. processAngle is not shown as the Fill angle (Atlas only engraves at 0°, so it is
/// written as 0). Not yet checked: a job engraved from a square.
/// </para>
/// </summary>
public static class MakeItPalette
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Largest colour square, mm (smaller when many colours share a page).</summary>
    public const double SquareMm = 10;
    /// <summary>Room under each square for its label, mm (at least).</summary>
    public const double LabelRoomMm = 3.4;
    /// <summary>Most squares across or down one page; more colours go on further pages (files).</summary>
    public const int MostPerSide = 15;
    /// <summary>MakeIt's canvas is 210 mm square, and Atlas centres its jobs at (105, 105).</summary>
    public const double CanvasCentreMm = 105;

    /// <summary>The colours an export holds, in the order the palette lays them out.</summary>
    public static IReadOnlyList<PaletteEntry> Chosen(IEnumerable<PaletteEntry> entries, bool verifiedOnly) =>
        Palette.ByColour(entries.Where(e => !verifiedOnly || e.Verified));

    /// <summary>
    /// The label of each square, in <see cref="Chosen"/> order: "row.column" as engraved under
    /// it, with "page/" in front when the palette needs more than one page ("2/1.3").
    /// </summary>
    public static IReadOnlyList<string> Labels(int count, double fieldMm)
    {
        const int perPage = MostPerSide * MostPerSide;
        int pages = Math.Max(1, (count + perPage - 1) / perPage);
        var labels = new List<string>(count);
        for (int pg = 0; pg < pages; pg++)
        {
            int n = Math.Min(perPage, count - pg * perPage);
            var lay = LayoutFor(n, fieldMm);
            for (int i = 0; i < n; i++)
                labels.Add((pages > 1 ? $"{pg + 1}/" : "") + $"{i / lay.Columns + 1}.{i % lay.Columns + 1}");
        }
        return labels;
    }

    /// <summary>Where the squares of one page go: inside the lens field, centred on the canvas.</summary>
    public sealed record Layout(int Columns, double Pitch, double Square, double Left, double Top)
    {
        public double X(int col) => Left + col * Pitch;
        public double Y(int row) => Top + row * Pitch;
    }

    /// <summary>
    /// A page for <paramref name="count"/> squares: 10 across while they fit at full size,
    /// smaller and more across beyond that, always within <paramref name="fieldMm"/> less 5 mm
    /// each side, so the whole page can be engraved without moving it.
    /// </summary>
    public static Layout LayoutFor(int count, double fieldMm)
    {
        // Full-size squares need SquareMm + room for the label.
        double block = Math.Max(40, (fieldMm > 0 ? fieldMm : 150) - 10);
        int cols = count <= 100 ? Math.Clamp(count, 1, 10) : Math.Min(MostPerSide, (int)Math.Ceiling(Math.Sqrt(count)));
        int rows = Math.Max(1, (count + cols - 1) / cols);
        double pitch = Math.Min(SquareMm + LabelRoomMm + 0.2, block / Math.Max(cols, rows));
        double square = pitch - LabelRoomMm - 0.2;
        double w = (cols - 1) * pitch + square, h = (rows - 1) * pitch + square + LabelRoomMm;
        return new Layout(cols, pitch, square, Math.Round(CanvasCentreMm - w / 2, 2), Math.Round(CanvasCentreMm - h / 2, 2));
    }

    /// <summary>Label text size: about 2.5 mm tall (MakeIt's "Hello" at 40 was 29.4 mm tall).</summary>
    private const double LabelFontSize = 3.4;
    private const string LabelColour = "#808080";

    /// <param name="Projects">One MakeIt project per page: the first holds up to 225 colours, then the next, and so on.</param>
    /// <param name="Legend">The .txt list: every square's label, colour and recipe, page by page.</param>
    /// <param name="Entries">Colours written, over all pages.</param>
    /// <param name="PulseWidthsSnapped">Pulse widths rounded to ones the source offers.</param>
    public sealed record Result(IReadOnlyList<string> Projects, string Legend, int Entries, int PulseWidthsSnapped)
    {
        public string Project => Projects[0];
    }

    public static Result Write(IReadOnlyList<PaletteEntry> entries, Material material, SourceProfile source, LensProfile lens,
                               bool verifiedOnly, DateTimeOffset when)
    {
        // In colour order (greys, then reds round to purples, light to dark in each): easier to
        // pick from than the window's verified-first order.
        var chosen = Chosen(entries, verifiedOnly).ToList();
        var legend = new StringBuilder();
        int snapped = 0;
        const int perPage = MostPerSide * MostPerSide;
        int pages = Math.Max(1, (chosen.Count + perPage - 1) / perPage);

        legend.AppendLine($"MOPAChroma Atlas colours for MakeIt — {material.DisplayName}");
        legend.AppendLine($"Laser: {source.Name} · lens: {lens.Name}. Recipes repeat only on this source and lens.");
        legend.AppendLine($"{(verifiedOnly ? "Repeated (verified) colours only" : "All good colours")} · {chosen.Count} squares" +
                          (pages > 1 ? $" on {pages} pages (one MakeIt project each)" : "") + $" · made {when:yyyy-MM-dd HH:mm}");
        legend.AppendLine("In MakeIt, choose the Lumos Ultra and its MOPA module (top left) first: without them MakeIt shows these");
        legend.AppendLine("speeds and line densities crossed out and hides frequency and pulse width.");
        legend.AppendLine("Squares are in colour order (greys and silvers first, then reds, oranges, golds, greens, blues, purples;");
        legend.AppendLine("light to dark within each) and run left to right, top to bottom, as listed below (row.column, as labelled).");
        legend.AppendLine("The labels are set to 0 % power; tick 'Ignore for processing' on them (or delete them) before engraving the palette.");

        var projects = new List<string>();
        for (int pg = 0; pg < pages; pg++)
        {
            var part = chosen.Skip(pg * perPage).Take(perPage).ToList();
            legend.AppendLine();
            if (pages > 1) legend.AppendLine($"Page {pg + 1} of {pages}");
            projects.Add(Page(part, pg, pages));
        }
        return new Result(projects, legend.ToString(), chosen.Count, snapped);

        string Page(List<PaletteEntry> chosen, int pg, int pages)
        {
        var lay = LayoutFor(chosen.Count, lens.FieldSizeMm);
        string canvasId = "canvas-" + Guid.NewGuid();
        var objects = new JsonArray();
        var processes = new JsonObject();
        var layerRows = new JsonArray();
        var labelRows = new List<JsonObject>();

        // "1.1" under each square, matching the .txt list. Text object as MakeIt 3.0.5 saved one
        // (Fabric i-text, Arial), at 0 % power so engraving the palette marks only the squares.
        void Label(int i, int row, int col)
        {
            string id = "el-" + Guid.NewGuid();
            objects.Add(new JsonObject
            {
                ["type"] = "i-text", ["version"] = "5.3.0", ["originX"] = "left", ["originY"] = "top",
                ["left"] = lay.X(col), ["top"] = lay.Y(row) + lay.Square + 0.3,
                ["width"] = 6.0, ["height"] = LabelFontSize * 0.734,
                ["fill"] = "", ["stroke"] = LabelColour, ["strokeWidth"] = 0, ["strokeDashArray"] = null, ["strokeLineCap"] = "butt",
                ["strokeDashOffset"] = 0, ["strokeLineJoin"] = "miter", ["strokeUniform"] = true, ["strokeMiterLimit"] = 4,
                ["scaleX"] = 1, ["scaleY"] = 1, ["angle"] = 0, ["flipX"] = false, ["flipY"] = false, ["opacity"] = 1, ["shadow"] = null,
                ["visible"] = true, ["backgroundColor"] = "", ["fillRule"] = "nonzero", ["paintFirst"] = "fill",
                ["globalCompositeOperation"] = "source-over", ["skewX"] = 0, ["skewY"] = 0,
                ["fontFamily"] = "Arial", ["fontWeight"] = "normal", ["fontSize"] = LabelFontSize, ["text"] = $"{row + 1}.{col + 1}",
                ["underline"] = false, ["overline"] = false, ["linethrough"] = false, ["textAlign"] = "left", ["fontStyle"] = "normal",
                ["lineHeight"] = 1.16, ["textBackgroundColor"] = "", ["charSpacing"] = 0, ["styles"] = new JsonArray(), ["direction"] = "ltr",
                ["path"] = null, ["pathStartOffset"] = 0, ["pathSide"] = "left", ["pathAlign"] = "baseline",
                ["id"] = id, ["name"] = "element", ["hasControls"] = true, ["isLock"] = false,
                ["originStroke"] = LabelColour, ["originStrokeForCut"] = LabelColour, ["sequence"] = 1000 + i,
                ["processMode"] = "engrave", ["isOffsetFill"] = false, ["isIgnoreWork"] = true, ["fixed"] = false, ["selectable"] = true,
                ["fontFamilyWin"] = "Arial", ["fontFamilyMac"] = "ArialMT", ["postscriptName"] = "ArialMT", ["subFontStyle"] = "Regular",
            });
            // Power 0: MakeIt 3.0.6 does not take "Ignore for processing" from isIgnoreWork (the box
            // showed unticked, 2026-10-01), so a label that did go into a job must not mark.
            JsonObject Gentle() => new() { ["power"] = 0, ["speed"] = 1000, ["repeat"] = 1 };
            processes[id] = new JsonObject
            {
                ["cut"] = Gentle(), ["engrave"] = Gentle(), ["fillEngrave"] = Gentle(),
                ["processMode"] = "engrave", ["isLock"] = false, ["strengthCutting"] = false, ["oneWayScan"] = true,
                ["lineDesity"] = 100, ["frequency"] = 48, ["pulseWidth"] = 200, ["dpi"] = 254, ["dotDuration"] = 230,
                ["breakPointObj"] = new JsonObject { ["breakPointNum"] = 2, ["breakPointSize"] = 0.4, ["isBreakPoint"] = false, ["isAutoBreakPoint"] = true },
                ["processAngle"] = 0, ["crystal25DParams"] = new JsonObject { ["distance"] = 0.01, ["sliceNumber"] = 10 },
                ["isOffsetFill"] = false, ["isIgnoreWork"] = true, ["cutMode"] = "default", ["type"] = "i-text", ["fixed"] = false, ["isEmboss"] = false,
            };
            labelRows.Add(new JsonObject
            {
                ["id"] = id, ["parentGroupID"] = "", ["base64"] = "", ["type"] = "shape", ["name"] = "editor.color_layer.text",
                ["color"] = LabelColour, ["selected"] = false, ["show"] = true, ["fixed"] = false,
            });
        }

        for (int i = 0; i < chosen.Count; i++)
        {
            var e = chosen[i];
            var s = e.Settings;
            string id = "el-" + Guid.NewGuid();
            string hex = e.Hex;
            int row = i / lay.Columns, col = i % lay.Columns;
            double pw = Palette.SnapPulseWidth(s.PulseWidthNs, source);
            if (Math.Abs(pw - s.PulseWidthNs) > 1e-9) snapped++;
            int lines = (int)Math.Round(s.LineDensityPerCm);
            int passes = Math.Max(1, s.Passes);

            objects.Add(new JsonObject
            {
                ["type"] = "rect", ["version"] = "5.3.0", ["originX"] = "left", ["originY"] = "top",
                ["left"] = lay.X(col), ["top"] = lay.Y(row),
                ["width"] = R(lay.Square), ["height"] = R(lay.Square),
                ["fill"] = hex, ["stroke"] = hex, ["strokeWidth"] = 0, ["strokeDashArray"] = null,
                ["strokeLineCap"] = "butt", ["strokeDashOffset"] = 0, ["strokeLineJoin"] = "miter", ["strokeUniform"] = true,
                ["strokeMiterLimit"] = 4, ["scaleX"] = 1, ["scaleY"] = 1, ["angle"] = 0, ["flipX"] = false, ["flipY"] = false,
                ["opacity"] = 1, ["shadow"] = null, ["visible"] = true, ["backgroundColor"] = "", ["fillRule"] = "nonzero",
                ["paintFirst"] = "fill", ["globalCompositeOperation"] = "source-over", ["skewX"] = 0, ["skewY"] = 0,
                ["rx"] = 0, ["ry"] = 0, ["id"] = id, ["name"] = "element", ["hasControls"] = true, ["isLock"] = false,
                ["originStroke"] = "#FFA500", ["originStrokeForCut"] = "#E61F19", ["sequence"] = i + 1,
                ["processMode"] = "fillEngrave", ["isOffsetFill"] = false, ["isIgnoreWork"] = false, ["fixed"] = false, ["selectable"] = true,
            });

            JsonObject Mode() => new() { ["power"] = R(s.PowerPercent), ["speed"] = R(s.SpeedMmPerS), ["repeat"] = passes };
            processes[id] = new JsonObject
            {
                ["cut"] = Mode(), ["engrave"] = Mode(), ["fillEngrave"] = Mode(),
                ["emboss"] = new JsonObject
                {
                    ["power"] = R(s.PowerPercent), ["speed"] = R(s.SpeedMmPerS), ["repeat"] = passes,
                    ["embossParams"] = new JsonObject
                    {
                        ["distance"] = 0.01, ["layer"] = 1, ["sliceNumber"] = 10, ["mode"] = "concave", ["angleType"] = "autoPlanning",
                        ["cleanPower"] = 0, ["cleanSpeed"] = 2, ["cleanRepeat"] = 1, ["cleanLayer"] = 1,
                        ["pulseWidth"] = R(pw), ["frequency"] = R(s.FrequencyKHz), ["lineDesity"] = lines,
                        ["processingCompleted"] = true, ["angleSwitch"] = false, ["zDownSwitch"] = false, ["cleanSwitch"] = false,
                    },
                },
                ["processMode"] = "fillEngrave", ["isLock"] = false, ["strengthCutting"] = false,
                // Atlas engraves every line in one direction, so the colours were made that way.
                ["oneWayScan"] = true,
                ["lineDesity"] = lines, ["frequency"] = R(s.FrequencyKHz), ["pulseWidth"] = R(pw),
                ["dpi"] = (int)Math.Round(lines * 2.54), ["dotDuration"] = 230,
                ["breakPointObj"] = new JsonObject { ["breakPointNum"] = 2, ["breakPointSize"] = 0.4, ["isBreakPoint"] = false, ["isAutoBreakPoint"] = true },
                // Atlas engraves every cell with level lines (0°), and MakeIt 3.0.6 does not take
                // its Fill angle from processAngle anyway (45 written, 0 shown; 2026-10-01).
                ["processAngle"] = 0,
                ["crystal25DParams"] = new JsonObject { ["distance"] = 0.01, ["sliceNumber"] = 10 },
                ["isOffsetFill"] = false, ["isIgnoreWork"] = false,
                ["cutMode"] = "default", ["type"] = "rect", ["isEmboss"] = false, ["fixed"] = false, ["radius"] = 0,
            };
            Label(i, row, col);

            // MakeIt groups layers by colour: a header row per colour, then its shapes.
            if (!layerRows.Any(r => r?["type"]?.GetValue<string>() == "color" && r?["id"]?.GetValue<string>() == hex))
                layerRows.Add(new JsonObject { ["type"] = "color", ["id"] = hex, ["color"] = hex, ["selected"] = false, ["show"] = true, ["fixed"] = false });
            layerRows.Add(new JsonObject
            {
                ["id"] = id, ["parentGroupID"] = "", ["base64"] = "", ["type"] = "shape", ["name"] = "editor.color_layer.rect",
                ["color"] = hex, ["selected"] = false, ["show"] = true, ["fixed"] = false,
            });

            legend.AppendLine($"{row + 1}.{col + 1}  {e.Name} ({hex}){(e.Verified ? " ✓ repeated" : "")}");
            legend.AppendLine($"      power {s.PowerPercent.ToString("0.#", Inv)} %, speed {s.SpeedMmPerS.ToString("0", Inv)} mm/s, " +
                              $"line density {lines}/cm, frequency {s.FrequencyKHz.ToString("0.#", Inv)} kHz, pulse width {pw.ToString("0.#", Inv)} ns, " +
                              $"{passes} pass{(passes == 1 ? "" : "es")}" +
                              (Math.Abs(s.FocusOffsetMm) > 1e-9 ? $", focus {s.FocusOffsetMm.ToString("+0.##;-0.##", Inv)} mm (set this yourself in MakeIt)" : "") +
                              $"  · cell {e.Best.CellLabel} · {e.Id}");
        }

        // Labels go in one grey layer of their own, after the colours.
        if (labelRows.Count > 0)
        {
            layerRows.Add(new JsonObject { ["type"] = "color", ["id"] = LabelColour, ["color"] = LabelColour, ["selected"] = false, ["show"] = true, ["fixed"] = false });
            foreach (var r in labelRows) layerRows.Add(r);
        }

        // The same fields, in the same order, as the project MakeIt 3.0.5 wrote.
        var project = new JsonObject
        {
            ["name"] = $"MOPAChroma {material.DisplayName}" + (pages > 1 ? $" ({pg + 1} of {pages})" : ""),
            ["version"] = "3.0.5",
            ["projectId"] = "project-" + Guid.NewGuid(),
            ["canvasList"] = new JsonArray(new JsonObject
            {
                ["id"] = canvasId, ["name"] = "canvas01", ["color"] = "#ffff00", ["objects"] = objects,
                ["workModeData"] = new JsonObject
                {
                    ["canvasID"] = canvasId, ["perimeter"] = 314.16, ["diameter"] = 100, ["workMode"] = "plane",
                    ["pathPlanning"] = "AUTO_MERGE", ["backgroundImage"] = null, ["baseplateDistance"] = 0, ["scanMode"] = "SCAN_ONE_WAY",
                },
                ["additional"] = new JsonObject { ["type"] = "2D" },
            }),
            ["processList"] = processes,
            ["time"] = when.ToUnixTimeMilliseconds(),
            ["currentCanvasId"] = canvasId,
            ["cover"] = "data:image/png;base64," + Convert.ToBase64String(Cover(chosen, lay)),
            ["layerDataList"] = new JsonArray(new JsonObject { ["id"] = canvasId, ["data"] = layerRows }),
        };
        return project.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
    }

    public static string FileName(Material m, SourceProfile s, LensProfile l, bool verifiedOnly, DateTimeOffset when) =>
        Path.ChangeExtension(LightBurnLibrary.FileName(m, s, l, verifiedOnly, when), ".wws");

    private static double R(double v) => Math.Round(v, 3);

    /// <summary>The project thumbnail MakeIt shows in its list: the canvas with its colour squares, 2 px per mm.</summary>
    private static byte[] Cover(IReadOnlyList<PaletteEntry> chosen, Layout lay)
    {
        const int k = 2; // px per mm
        int w = (int)(2 * CanvasCentreMm * k), h = w;
        var px = new byte[w * h * 3];
        Array.Fill(px, (byte)235);
        for (int i = 0; i < chosen.Count; i++)
        {
            var c = Color.ColorConvert.ToSrgb(chosen[i].Color);
            int x0 = (int)(lay.X(i % lay.Columns) * k), y0 = (int)(lay.Y(i / lay.Columns) * k);
            for (int y = Math.Max(0, y0); y < y0 + lay.Square * k && y < h; y++)
                for (int x = Math.Max(0, x0); x < x0 + lay.Square * k && x < w; x++)
                {
                    int j = (y * w + x) * 3;
                    px[j] = c.R; px[j + 1] = c.G; px[j + 2] = c.B;
                }
        }
        return Png(w, h, px);
    }

    /// <summary>A plain 8-bit RGB PNG.</summary>
    internal static byte[] Png(int w, int h, byte[] rgb)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            for (int y = 0; y < h; y++)
            {
                z.WriteByte(0);
                z.Write(rgb, y * w * 3, w * 3);
            }
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)w); BE(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 2; // 8 bits, RGB
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();

        static void BE(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; BE(len, 0, (uint)data.Length); s.Write(len);
            var td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            s.Write(td);
            var crc = new byte[4]; BE(crc, 0, Crc(td)); s.Write(crc);
        }
        static uint Crc(byte[] d)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte b in d)
            {
                c ^= b;
                for (int i = 0; i < 8; i++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return c ^ 0xFFFFFFFF;
        }
    }
}
