using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using MOPAChroma.Atlas;
using LumosUltra.Jobs;
namespace MOPAChroma.Export;

/// <summary>
/// Writes atlas colours as a LightBurn material library (.clb) plus a sidecar .json with
/// the full record. One library per material, source and lens: LightBurn ties a library
/// to the laser it was saved for, and a recipe only repeats on the same source and lens.
/// <para>
/// Layout (from LightBurn's own library files): LightBurnLibrary → Material
/// (name) → Entry (Thickness, Desc) → CutSetting type="Scan" with &lt;param Value=""/&gt;
/// children. Units in the file: speed mm/s, power %, frequency Hz, QPulseWidth ns,
/// interval mm. Thickness -1 means "No thickness" (surface marking).
/// </para>
/// <para>
/// Entry names never change once published: "colour name · kHz/ns · id". LightBurn re-links
/// layers by material, thickness and description, so a re-measured colour (new reading,
/// newly verified) must not rename its entry. Readings, hex and status go in the sidecar.
/// </para>
/// <para>NOT yet checked against a library saved by Lee's own LightBurn (tests T5-T9 in the research report).</para>
/// </summary>
public static class LightBurnLibrary
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record Result(string Clb, string SidecarJson, int Entries, int PulseWidthsSnapped);

    public static Result Write(IReadOnlyList<PaletteEntry> entries, Material material, SourceProfile source, LensProfile lens,
                               bool verifiedOnly, DateTimeOffset when)
    {
        var chosen = entries.Where(e => !verifiedOnly || e.Verified).ToList();
        string thickness = material.ThicknessMm > 0 ? material.ThicknessMm.ToString("0.0000", Inv) : "-1.0000";
        int snapped = 0;

        var sb = new StringBuilder();
        var xs = new XmlWriterSettings { Indent = true, IndentChars = "    ", Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
        using (var sw = new Utf8StringWriter(sb))
        using (var w = XmlWriter.Create(sw, xs))
        {
            w.WriteStartDocument();
            w.WriteComment($" MOPAChroma Atlas export · material={material.DisplayName} · source={source.Name} · lens={lens.Name} · " +
                           $"{(verifiedOnly ? "verified colours only" : "all good colours, verified and not")} · {chosen.Count} entries · " +
                           $"generated {when:yyyy-MM-dd HH:mm} · full record in the .json beside this file. " +
                           "Recipes repeat only on the same source and lens; elsewhere they are a starting point. ");
            w.WriteStartElement("LightBurnLibrary");
            w.WriteAttributeString("DisplayName", $"MOPAChroma · {source.Name} · {lens.Name}");
            w.WriteStartElement("Material");
            w.WriteAttributeString("name", material.DisplayName);
            foreach (var e in chosen)
            {
                var s = e.Settings;
                double pw = Palette.SnapPulseWidth(s.PulseWidthNs, source);
                if (Math.Abs(pw - s.PulseWidthNs) > 1e-9) snapped++;
                string desc = Desc(e);
                w.WriteStartElement("Entry");
                w.WriteAttributeString("Thickness", thickness);
                w.WriteAttributeString("Desc", desc);
                w.WriteStartElement("CutSetting");
                w.WriteAttributeString("type", "Scan");
                Param(w, "index", "0");
                Param(w, "name", "");
                Param(w, "LinkPath", $"{material.DisplayName}/{thickness}/{desc}");
                Param(w, "minPower", N(s.PowerPercent));
                Param(w, "maxPower", N(s.PowerPercent));
                Param(w, "speed", N(s.SpeedMmPerS));
                Param(w, "frequency", Math.Round(s.FrequencyKHz * 1000).ToString(Inv));
                Param(w, "overrideFrequency", "1");
                Param(w, "QPulseWidth", N(pw));
                Param(w, "interval", double.IsFinite(s.LinePitchMm) ? s.LinePitchMm.ToString("0.#####", Inv) : "0.01");
                Param(w, "numPasses", Math.Max(1, s.Passes).ToString(Inv));
                Param(w, "angle", N(s.HatchAngleDeg));
                Param(w, "crossHatch", "0");
                Param(w, "bidir", "1");
                w.WriteEndElement(); // CutSetting
                w.WriteEndElement(); // Entry
            }
            w.WriteEndElement(); // Material
            w.WriteEndElement(); // LightBurnLibrary
            w.WriteEndDocument();
        }

        var sidecar = new
        {
            generator = "MOPAChroma Atlas",
            generated = when,
            material = new { material.Id, material.DisplayName, material.Grade, material.Finish, material.ThicknessMm, material.Supplier, material.Sku },
            source = new { source.Id, source.Name, source.RatedPowerW, pulseWidthsNs = source.PulseWidthsNs, evidence = source.Evidence.ToString() },
            lens = new { lens.Id, lens.Name, lens.FieldSizeMm, lens.SpotDiameterUm },
            verifiedOnly,
            note = "Recipes repeat only on the same source and lens. Colours: CIE L*a*b* (D65); method says how each was read.",
            entries = chosen.Select(e => new
            {
                id = e.Id, name = e.Name, desc = Desc(e), hex = e.Hex,
                lab = new { e.Color.L, a = e.Color.A, b = e.Color.B },
                method = e.Method.ToString(), status = e.Repeat.Status.ToString(), repeats = e.Repeat.Repeats,
                repeatDeltaE00 = e.Repeat.BestDeltaE00, readings = e.Readings,
                settings = new
                {
                    powerPercent = e.Settings.PowerPercent, speedMmPerS = e.Settings.SpeedMmPerS, frequencyKHz = e.Settings.FrequencyKHz,
                    pulseWidthNs = Palette.SnapPulseWidth(e.Settings.PulseWidthNs, source), pulseWidthRecordedNs = e.Settings.PulseWidthNs,
                    lineDensityPerCm = e.Settings.LineDensityPerCm, linePitchMm = e.Settings.LinePitchMm, passes = e.Settings.Passes,
                    hatchAngleDeg = e.Settings.HatchAngleDeg, focusOffsetMm = e.Settings.FocusOffsetMm,
                },
                cell = e.Best.CellLabel, measured = e.Best.MeasuredUtc,
            }),
        };
        var json = JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true });
        return new Result(sb.ToString(), json, chosen.Count, snapped);
    }

    /// <summary>"Deep blue · 250k/6ns · A1B2C3": never changes once published (see the class notes).</summary>
    public static string Desc(PaletteEntry e) =>
        $"{e.Name} · {e.Settings.FrequencyKHz.ToString("0.#", Inv)}k/{e.Settings.PulseWidthNs.ToString("0.#", Inv)}ns · {e.Id}";

    /// <summary>A file name in the community style: machine, lens, what is in it, date.</summary>
    public static string FileName(Material m, SourceProfile s, LensProfile l, bool verifiedOnly, DateTimeOffset when)
    {
        string Clean(string x) => string.Concat(x.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
        return $"MOPAChroma_{Clean(m.DisplayName)}_{Clean(s.Name)}_{Clean(l.Name)}_{(verifiedOnly ? "verified" : "all")}_{when:yyyy-MM-dd}.clb";
    }

    private static void Param(XmlWriter w, string name, string value)
    {
        w.WriteStartElement(name);
        w.WriteAttributeString("Value", value);
        w.WriteEndElement();
    }

    private static string N(double v) => v.ToString("0.###", Inv);

    private sealed class Utf8StringWriter(StringBuilder sb) : StringWriter(sb, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
