using System.Security.Cryptography;
using System.Text;
using MOPAChroma.Color;
using MOPAChroma.Laser;

namespace MOPAChroma.Atlas;

/// <summary>
/// One colour the atlas can make: a recipe (settings) and the best reading of what it
/// produced. This is the single record every export draws from, so the Atlas's own jobs,
/// a LightBurn library and anything later all describe the same thing.
/// </summary>
/// <param name="Id">Stable short id from the material, source, lens and settings: unchanged by re-measuring, so exports keep their links.</param>
/// <param name="Name">A plain colour name from the measured colour ("Deep blue", "Light gold").</param>
/// <param name="Settings">The recipe.</param>
/// <param name="Color">The best reading: an instrument or calibrated image, else the most recent picture or scan.</param>
/// <param name="Method">How that reading was taken.</param>
/// <param name="Repeat">Whether the recipe gave the same colour on another card.</param>
/// <param name="Readings">How many good readings of this recipe the atlas holds.</param>
/// <param name="Best">The sample the colour comes from (its grid and cell).</param>
public sealed record PaletteEntry(
    string Id, string Name, MopaSettings Settings, Lab Color, MeasureMethod Method,
    RepeatCheck Repeat, int Readings, Sample Best)
{
    public string Hex => ColorConvert.ToSrgb(Color).ToHex();
    public bool Verified => Repeat.Status == RepeatStatus.Verified;
}

/// <summary>
/// The colours worth using for one material, source and lens: only cells that came out
/// Good with a colour reading (no-mark, uneven and damaged cells are left out), one entry
/// per recipe, verified colours first.
/// </summary>
public static class Palette
{
    public static IReadOnlyList<PaletteEntry> Build(IReadOnlyList<Sample> all, string materialId, string sourceId, string lensId)
    {
        var good = all.Where(s => s.MaterialId == materialId && s.SourceId == sourceId && s.LensId == lensId
                                  && s.Outcome == Outcome.Good && s.Color is not null).ToList();
        // Group repeats of the same recipe (within MakeIt's typing precision).
        var groups = new List<List<Sample>>();
        foreach (var s in good)
        {
            var g = groups.FirstOrDefault(x => Repeatability.SameSettings(x[0].Settings, s.Settings));
            if (g is null) groups.Add([s]); else g.Add(s);
        }
        var entries = new List<PaletteEntry>();
        foreach (var g in groups)
        {
            var best = g.OrderByDescending(s => Rank(s.Method)).ThenByDescending(s => s.MeasuredUtc ?? DateTimeOffset.MinValue).First();
            var checks = g.Select(s => Repeatability.Check(s, all)).ToList();
            var repeat = checks.FirstOrDefault(c => c.Status == RepeatStatus.Verified)
                         ?? checks.FirstOrDefault(c => c.Status == RepeatStatus.RepeatDiffers)
                         ?? checks[0];
            entries.Add(new PaletteEntry(RecipeId(materialId, sourceId, lensId, best.Settings), ColorNames.Name(best.Color!.Value),
                                         best.Settings, best.Color!.Value, best.Method, repeat, g.Count, best));
        }
        return entries
            .OrderBy(e => e.Repeat.Status switch { RepeatStatus.Verified => 0, RepeatStatus.NotRepeated => 1, _ => 2 })
            .ThenBy(e => ColourFamily(e.Color))
            .ThenByDescending(e => e.Color.L)
            .ToList();
    }

    /// <summary>Below this chroma (C*ab) a colour reads as grey or silver, and its hue angle is noise.</summary>
    public const double NeutralChroma = 6;

    /// <summary>
    /// Sort key that groups colours the way the eye does: greys and silvers first (-1), then
    /// twelve 30° hue families starting at red (0 = reds, 1 = oranges/browns, 2 = golds…).
    /// Sorting by raw hue angle scatters near-greys everywhere, because a grey's hue is noise.
    /// </summary>
    public static int ColourFamily(Lab c)
    {
        double chroma = Math.Sqrt(c.A * c.A + c.B * c.B);
        if (chroma < NeutralChroma) return -1;
        double h = (Math.Atan2(c.B, c.A) * 180 / Math.PI + 360 + 15) % 360; // red centred on 0°
        return (int)(h / 30);
    }

    /// <summary>Colours in colour order: by family (greys first), light to dark within each.</summary>
    public static IReadOnlyList<PaletteEntry> ByColour(IEnumerable<PaletteEntry> entries) =>
        entries.OrderBy(e => ColourFamily(e.Color)).ThenByDescending(e => e.Color.L).ToList();

    /// <summary>
    /// Which reading of a recipe the palette uses: an instrument or a calibrated image first;
    /// otherwise simply the most recent picture, scan or photo alike. A flatbed scan was once
    /// ranked above photos, but on brushed and polished metal Lee's scans came out washed out
    /// and flat while a USB camera with angled light gave true colour (2026-10-02), so a later,
    /// better photo must be able to replace an earlier scan.
    /// </summary>
    private static int Rank(MeasureMethod m) => m switch
    {
        MeasureMethod.Instrument => 3,
        MeasureMethod.CalibratedImage => 2,
        MeasureMethod.Scanner or MeasureMethod.Estimated => 1,
        _ => 0,
    };

    /// <summary>"A" and five hex digits from the material, source, lens and rounded settings.</summary>
    public static string RecipeId(string materialId, string sourceId, string lensId, MopaSettings s)
    {
        var key = string.Join('|', materialId, sourceId, lensId,
            Math.Round(s.PowerPercent, 1), Math.Round(s.SpeedMmPerS), Math.Round(s.FrequencyKHz, 1), Math.Round(s.PulseWidthNs, 1),
            Math.Round(s.LineDensityPerCm), s.Passes, Math.Round(s.HatchAngleDeg), Math.Round(s.FocusOffsetMm, 2));
        var h = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        return "A" + Convert.ToHexString(h)[..5];
    }

    /// <summary>The nearest pulse width the source accepts (ties go to the shorter), or the value itself if the source lists none.</summary>
    public static double SnapPulseWidth(double ns, SourceProfile source) =>
        source.PulseWidthsNs.Count == 0 ? ns
            : source.PulseWidthsNs.OrderBy(w => Math.Abs(w - ns)).ThenBy(w => w).First();
}

/// <summary>Plain names for oxide colours, from a measured L*a*b* colour.</summary>
public static class ColorNames
{
    public static string Name(Lab c)
    {
        // CIELAB hue angles (not the colour wheel's): red ≈ 40°, yellow ≈ 100°, green ≈ 135°,
        // cyan ≈ 195°, blue ≈ 280-305°, magenta ≈ 330°.
        string tone = c.L < 40 ? "Deep " : c.L > 72 ? "Light " : "";
        if (c.Chroma < 7)
            return c.L > 72 ? "Silver" : c.L > 45 ? "Grey" : c.L > 25 ? "Dark grey" : "Black";
        double h = c.HueDegrees;
        string hue =
            h < 25 || h >= 340 ? "rose"
            : h < 50 ? "red"
            : h < 72 ? (c.L < 50 ? "bronze" : "orange")
            : h < 100 ? (c.L < 45 ? "brown" : c.L > 75 ? "straw" : "gold")
            : h < 125 ? "yellow-green"
            : h < 165 ? "green"
            : h < 220 ? "teal"
            : h < 300 ? "blue"
            : "purple";
        if (hue is "straw" or "brown" or "bronze") tone = "";
        string muted = c.Chroma < 15 ? "muted " : "";
        var name = (tone + muted + hue).Trim();
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
