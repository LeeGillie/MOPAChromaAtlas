namespace LumosUltra.Jobs;

/// <summary>
/// A tiny single-line font for engraving short labels ("Grid 29") as vector strokes: each
/// glyph is a few polylines on a 4 × 6 unit box (cap height 6, x-height 4, baseline 0, y up).
/// Only the characters labels need: digits, space, and the letters of "Grid".
/// </summary>
public static class StrokeFont
{
    private sealed record Glyph(double Advance, double[][] Strokes);

    private static readonly Dictionary<char, Glyph> Glyphs = new()
    {
        ['G'] = new(5.5, [[4, 5, 3, 6, 1, 6, 0, 5, 0, 1, 1, 0, 3, 0, 4, 1, 4, 3, 2.2, 3]]),
        ['r'] = new(4.0, [[0, 4, 0, 0], [0, 2.4, 1.6, 4, 3, 4]]),
        ['i'] = new(2.2, [[0.6, 0, 0.6, 4], [0.6, 5.3, 0.6, 5.8]]),
        ['d'] = new(5.0, [[3.5, 6, 3.5, 0], [3.5, 1, 2.5, 0, 1, 0, 0, 1, 0, 3, 1, 4, 2.5, 4, 3.5, 3]]),
        [' '] = new(3.0, []),
        ['0'] = new(5.5, [[1, 0, 3, 0, 4, 1, 4, 5, 3, 6, 1, 6, 0, 5, 0, 1, 1, 0]]),
        ['1'] = new(5.5, [[1, 5, 2, 6, 2, 0], [1, 0, 3, 0]]),
        ['2'] = new(5.5, [[0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 0, 0, 4, 0]]),
        ['3'] = new(5.5, [[0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 3, 3, 4, 2, 4, 1, 3, 0, 1, 0, 0, 1], [1.5, 3, 3, 3]]),
        ['4'] = new(5.5, [[3, 0, 3, 6, 0, 2, 4, 2]]),
        ['5'] = new(5.5, [[4, 6, 0, 6, 0, 3.5, 3, 3.5, 4, 2.5, 4, 1, 3, 0, 1, 0, 0, 1]]),
        ['6'] = new(5.5, [[4, 5, 3, 6, 1, 6, 0, 5, 0, 1, 1, 0, 3, 0, 4, 1, 4, 2.5, 3, 3.5, 0, 3.5]]),
        ['7'] = new(5.5, [[0, 6, 4, 6, 1.5, 0]]),
        ['8'] = new(5.5, [[1, 3, 0, 4, 0, 5, 1, 6, 3, 6, 4, 5, 4, 4, 3, 3, 1, 3, 0, 2, 0, 1, 1, 0, 3, 0, 4, 1, 4, 2, 3, 3]]),
        ['9'] = new(5.5, [[4, 2.5, 1, 2.5, 0, 3.5, 0, 5, 1, 6, 3, 6, 4, 5, 4, 1, 3, 0, 1, 0, 0, 1]]),
    };

    public static bool CanDraw(string text) => text.All(Glyphs.ContainsKey);

    /// <summary>Width of <paramref name="text"/> at a cap height of <paramref name="height"/>, without the last glyph's spacing.</summary>
    public static double Width(string text, double height)
    {
        double units = text.Sum(c => Glyphs[c].Advance) - 1.5;
        return Math.Max(0, units) * height / 6;
    }

    /// <summary>
    /// The text as polylines in machine millimetres (y grows downwards, as on the bed), with
    /// its left end at <paramref name="x"/> and its baseline at <paramref name="baselineY"/>.
    /// </summary>
    public static List<List<(double X, double Y)>> Polylines(string text, double x, double baselineY, double height)
    {
        double k = height / 6;
        var lines = new List<List<(double, double)>>();
        foreach (char c in text)
        {
            var g = Glyphs[c];
            foreach (var s in g.Strokes)
            {
                var pl = new List<(double, double)>();
                for (int i = 0; i + 1 < s.Length; i += 2) pl.Add((x + s[i] * k, baselineY - s[i + 1] * k));
                lines.Add(pl);
            }
            x += g.Advance * k;
        }
        return lines;
    }
}
