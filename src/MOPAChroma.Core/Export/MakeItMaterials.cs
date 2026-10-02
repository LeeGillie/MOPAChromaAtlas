using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MOPAChroma.Atlas;
using MOPAChroma.Laser;

namespace MOPAChroma.Export;

/// <summary>
/// Writes atlas colours as MakeIt custom materials: a file for MakeIt's Material preview →
/// Import. Each colour becomes a material named after it, whose Fill settings are the recipe
/// and whose preview picture is the colour itself. In MakeIt, select artwork and pick the
/// colour from the Material list to give it that recipe.
/// <para>
/// Format: the plain JSON MakeIt 3.0.6 writes on Material preview → Export (an array of
/// materials; Lee's export of 2026-10-01, 17 custom materials). Per material: id, type
/// "custom", isDef false, deviceType "lumosUltra", power "100w" (the source), title,
/// thickness "/" (none), version "3.0.6", isGalvanometer, reference.referenceImg (the preview,
/// a PNG data URL), and mode with one block per process. The colour recipe goes in
/// mode.fillEngrave (power %, speed mm/s, repeat = passes, lineDesity lines/cm, frequency kHz,
/// pulseWidth ns, advanced.processAngle) and the same power, speed, frequency and pulse width
/// in mode.engrave (Line); the other processes keep MakeIt's defaults from that export.
/// </para>
/// <para>Checked 2026-10-01: Import adds these beside the user's own materials (Customization
/// tab), each showing its colour as the picture; choosing one for a shape sets every Fill
/// setting (power, speed, passes, pulse width, frequency, line density) and shows the colour
/// under Preview. Re-importing updates a material with the same id in place (no duplicate;
/// a changed power and frequency came through), so each colour keeps one entry however often
/// the palette is exported, and a re-measured colour's new name and picture replace the old.</para>
/// </summary>
public static class MakeItMaterials
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record Result(string Json, int Entries, int PulseWidthsSnapped);

    /// <summary>
    /// Every colour of the palette export as a material, in palette order and named after its
    /// palette square, so the MakeIt palette project and the material list match.
    /// </summary>
    public static Result Write(IReadOnlyList<PaletteEntry> entries, Material material, SourceProfile source, LensProfile lens,
                               bool verifiedOnly, DateTimeOffset when)
    {
        var chosen = MakeItPalette.Chosen(entries, verifiedOnly);
        return Write(chosen, MakeItPalette.Labels(chosen.Count, lens.FieldSizeMm), material, source, when, lens);
    }

    /// <summary>The given colours as materials, each titled with the label given for it.</summary>
    /// <param name="entries">Colours, in the order to write them.</param>
    /// <param name="labels">The palette label for each entry ("1.3"), so a material and its square in the MakeIt palette share a name.</param>
    /// <param name="material">The atlas material (its name starts each title).</param>
    /// <param name="source">The laser source (MakeIt's "power", e.g. "100w").</param>
    /// <param name="when">When the export was made.</param>
    /// <param name="lens">The lens, part of each material's stable id.</param>
    public static Result Write(IReadOnlyList<PaletteEntry> entries, IReadOnlyList<string> labels, Material material, SourceProfile source,
                               DateTimeOffset when, LensProfile? lens = null)
    {
        var list = new JsonArray();
        int snapped = 0;
        string power = $"{Math.Round(source.RatedPowerW > 0 ? source.RatedPowerW : 100).ToString(Inv)}w";
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var s = e.Settings;
            double pw = Palette.SnapPulseWidth(s.PulseWidthNs, source);
            if (Math.Abs(pw - s.PulseWidthNs) > 1e-9) snapped++;
            int lines = (int)Math.Round(s.LineDensityPerCm);
            int passes = Math.Max(1, s.Passes);
            double p = Math.Round(s.PowerPercent, 1), v = Math.Round(s.SpeedMmPerS, 1), f = Math.Round(s.FrequencyKHz, 1);

            var mode = new JsonObject
            {
                ["engrave"] = new JsonObject { ["power"] = p, ["speed"] = v, ["repeat"] = passes, ["frequency"] = f, ["pulseWidth"] = pw },
                ["fillEngrave"] = new JsonObject
                {
                    ["power"] = p, ["speed"] = v, ["repeat"] = passes,
                    // Atlas engraves every line the same way round, at 0°.
                    ["oneWayScan"] = true, ["lineDesity"] = lines, ["dpi"] = 254,
                    ["advanced"] = new JsonObject { ["processAngle"] = 0, ["processDirection"] = "fixed", ["enableCross"] = false, ["enablePause"] = false, ["pauseDuration"] = 0.1 },
                    ["zDown"] = new JsonObject { ["enable"] = false, ["distance"] = 0.01, ["gapLayer"] = 1 },
                    ["frequency"] = f, ["pulseWidth"] = pw, ["isOffsetFill"] = false,
                },
                ["bitmapEngrave"] = new JsonObject
                {
                    ["power"] = 1, ["speed"] = 2, ["repeat"] = 1, ["imgRevertMode"] = "jarvis", ["oneWayScan"] = true, ["lineDesity"] = 100,
                    ["dpi"] = 254, ["dotDuration"] = 230,
                    ["advanced"] = new JsonObject { ["processAngle"] = 0, ["processDirection"] = "fixed", ["enableCross"] = false, ["enablePause"] = false, ["pauseDuration"] = 0.1 },
                    ["frequency"] = 48, ["pulseWidth"] = 200,
                },
                ["cut"] = new JsonObject
                {
                    ["power"] = 1, ["speed"] = 2, ["repeat"] = 1, ["strengthCutting"] = false,
                    ["breakPointObj"] = new JsonObject { ["breakPointNum"] = 2, ["breakPointSize"] = 0.4, ["isBreakPoint"] = false, ["isAutoBreakPoint"] = true },
                    ["zDown"] = new JsonObject { ["enable"] = false, ["distance"] = 0.01, ["gapLayer"] = 1 },
                    ["frequency"] = 48, ["pulseWidth"] = 200, ["cutMode"] = "default",
                    ["cutModeParams"] = new JsonObject { ["lineDesity"] = 20, ["distance"] = 0.4 },
                },
                ["emboss"] = new JsonObject
                {
                    ["power"] = 0, ["speed"] = 2, ["repeat"] = 1, ["frequency"] = 48, ["pulseWidth"] = 200, ["lineDesity"] = 100,
                    ["advanced"] = new JsonObject { ["processAngle"] = 0 },
                    ["embossParams"] = new JsonObject
                    {
                        ["distance"] = 0.01, ["layer"] = 1, ["sliceNumber"] = 10, ["mode"] = "concave", ["angleType"] = "autoPlanning",
                        ["cleanPower"] = 0, ["cleanSpeed"] = 2, ["cleanRepeat"] = 1, ["cleanLayer"] = 1, ["pulseWidth"] = 200, ["frequency"] = 48,
                        ["lineDesity"] = 100, ["processingCompleted"] = true, ["processAngle"] = 0, ["zDownSwitch"] = false, ["cleanSwitch"] = false,
                    },
                },
                ["crystal"] = new JsonObject { ["power"] = 100, ["speed"] = 100, ["dotDuration"] = 70, ["frequency"] = 80, ["zDpi"] = 508, ["xyDpi"] = 508 },
                ["crystal25DParams"] = new JsonObject { ["power"] = 100, ["speed"] = 100, ["distance"] = 0.01, ["sliceNumber"] = 10 },
            };

            list.Add(new JsonObject
            {
                ["mode"] = mode,
                ["isGalvanometer"] = true,
                // The same colour always gets the same id, so a later export is the same material.
                ["id"] = StableId($"{material.Id}|{source.Id}|{lens?.Id}|{e.Id}").ToString(),
                ["type"] = "custom",
                ["isDef"] = false,
                ["deviceType"] = "lumosUltra",
                ["power"] = power,
                ["title"] = Title(e, labels[i], material),
                ["reference"] = new JsonObject
                {
                    ["param"] = new JsonObject(),
                    ["originalImg"] = "",
                    ["referenceImg"] = "data:image/png;base64," + Convert.ToBase64String(Swatch(e)),
                },
                // No thickness: Atlas colours are made at the focus the user sets, not by MakeIt's thickness offset.
                ["thickness"] = "/",
                ["thicknessList"] = new JsonArray("/"),
                ["version"] = "3.0.6",
            });
        }
        return new Result(list.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), entries.Count, snapped);
    }

    /// <summary>
    /// A name-based UUID (version 5 layout, RFC 4122 variant): the same colour always gets the
    /// same id, and it is a well-formed UUID. MakeIt accepted random (version 4) ids but showed
    /// nothing from an import whose ids were raw hash bytes (2026-10-01), so the version and
    /// variant bits must be right.
    /// </summary>
    internal static Guid StableId(string key)
    {
        var b = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("MOPAChroma material " + key))[..16];
        b[6] = (byte)((b[6] & 0x0F) | 0x50); // version 5 (big-endian byte order)
        b[8] = (byte)((b[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(b, bigEndian: true);
    }

    /// <summary>"Titanium 1.3 Deep blue ✓": material, the palette square's label, the colour; ✓ when repeated.</summary>
    public static string Title(PaletteEntry e, string label, Material m) =>
        $"{m.Name} {label} {e.Name}{(e.Verified ? " ✓" : "")}";

    public static string FileName(Material m, SourceProfile s, LensProfile l, bool verifiedOnly, DateTimeOffset when) =>
        Path.GetFileNameWithoutExtension(LightBurnLibrary.FileName(m, s, l, verifiedOnly, when)) + "_MakeIt-materials.json";

    /// <summary>The material's preview picture: a 400 px square of the colour on a light frame.</summary>
    private static byte[] Swatch(PaletteEntry e)
    {
        const int n = 400, frame = 24;
        var c = Color.ColorConvert.ToSrgb(e.Color);
        var px = new byte[n * n * 3];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool inside = x >= frame && y >= frame && x < n - frame && y < n - frame;
                int j = (y * n + x) * 3;
                px[j] = inside ? c.R : (byte)240; px[j + 1] = inside ? c.G : (byte)240; px[j + 2] = inside ? c.B : (byte)240;
            }
        return MakeItPalette.Png(n, n, px);
    }
}
