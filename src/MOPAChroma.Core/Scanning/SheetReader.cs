using MOPAChroma.Atlas;
using MOPAChroma.Grids;

namespace MOPAChroma.Scanning;

/// <summary>One grid found on a scanned sheet: which grid it is, where, and its cell readings.</summary>
public sealed record SheetFind(TestGrid Grid, GridDetection Detection, ScanMeasurement Reading);

/// <summary>
/// Reads every numbered grid in one picture of a whole sheet (a flatbed scan, usually).
/// Finds a grid, reads its engraved number, matches it to the atlas, then blanks that
/// part of the picture and looks again, until nothing more is found.
/// Only grids engraved with corner crosses and a number can be found this way; older
/// grids are read one at a time as before.
/// </summary>
public static class SheetReader
{
    /// <summary>Most grids looked for in one picture; a guard against a runaway loop.</summary>
    public const int MaxGrids = 40;

    public static IReadOnlyList<SheetFind> FindAll(RgbImage img, AtlasDocument doc, Action<string>? progress = null,
                                                   CancellationToken cancel = default)
    {
        // Each grid's shape (rows, columns, proportions) needs its own search; usually there are only a few.
        var shapes = doc.Grids
            .Where(g => g.Number is >= 1 and <= CardId.MaxNumber && CardId.For(g.Layout) is not null)
            .GroupBy(g => (g.Layout.Rows, g.Layout.Columns, Math.Round(g.Layout.CellSizeMm, 2), Math.Round(g.Layout.GapMm, 2)))
            .Select(gr => (Layout: gr.First().Layout, Grids: gr.ToDictionary(g => g.Number)))
            .ToList();
        var found = new List<SheetFind>();
        if (shapes.Count == 0) return found;

        var work = new RgbImage(img.Width, img.Height, (byte[])img.Pixels.Clone());
        var fill = Background(img);
        var dead = new HashSet<int>(); // shapes that found nothing on the latest pass
        for (int pass = 0; pass < MaxGrids && found.Count < MaxGrids; pass++)
        {
            cancel.ThrowIfCancellationRequested();
            bool any = false;
            for (int s = 0; s < shapes.Count && found.Count < MaxGrids; s++)
            {
                if (dead.Contains(s)) continue;
                cancel.ThrowIfCancellationRequested();
                var (layout, grids) = shapes[s];
                var ds = GridDetect.DetectEach(work, layout.Rows, layout.Columns, CardMarks.AtlasCorners(layout))
                                   .Where(d => d.PlacementCertain).ToList();
                if (ds.Count == 0) { dead.Add(s); continue; }
                var numbers = ds.Select(d => CardId.Read(work, d.Grid, layout)).ToList();
                // Blank them whatever they were, so the next search finds something else.
                foreach (var d in ds) Blank(work, d.Grid, layout, fill);
                any = true;
                for (int k = 0; k < ds.Count; k++)
                {
                    var (d, n) = (ds[k], numbers[k]);
                    if (n is { } num && grids.TryGetValue(num, out var grid) && found.All(f => f.Grid.Id != grid.Id))
                    {
                        found.Add(new SheetFind(grid, d, ScanMeasure.Measure(img, d.Grid)));
                        progress?.Invoke($"Found grid #{num}: {grid.Title}");
                    }
                    else
                        progress?.Invoke(n is null ? "Found a grid without a readable number; skipped it."
                                                   : $"Found grid #{n}, which is not in this atlas or was already read; skipped it.");
                }
            }
            if (!any) break;
        }
        return found;
    }

    /// <summary>Paints over a found grid, its crosses and its number, with the sheet's background.</summary>
    private static void Blank(RgbImage img, GridGeometry g, GridLayout l, (byte R, byte G, byte B) fill)
    {
        double m = CornerMarks.OffsetPitches(l) + CornerMarks.HalfSizePitches(l) + 0.6;
        var quad = new[]
        {
            g.CentreAt(-m, -m), g.CentreAt(-m, l.Columns - 1 + m),
            g.CentreAt(l.Rows - 1 + m, l.Columns - 1 + m), g.CentreAt(l.Rows - 1 + m, -m),
        };
        int y0 = Math.Max(0, (int)Math.Floor(quad.Min(p => p.Y))), y1 = Math.Min(img.Height - 1, (int)Math.Ceiling(quad.Max(p => p.Y)));
        int x0 = Math.Max(0, (int)Math.Floor(quad.Min(p => p.X))), x1 = Math.Min(img.Width - 1, (int)Math.Ceiling(quad.Max(p => p.X)));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!Inside(quad, x + 0.5, y + 0.5)) continue;
                int i = (y * img.Width + x) * 3;
                img.Pixels[i] = fill.R; img.Pixels[i + 1] = fill.G; img.Pixels[i + 2] = fill.B;
            }
    }

    private static bool Inside(PointD[] q, double x, double y)
    {
        bool? sign = null;
        for (int k = 0; k < q.Length; k++)
        {
            var a = q[k]; var b = q[(k + 1) % q.Length];
            double cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
            if (Math.Abs(cross) < 1e-9) continue;
            bool s = cross > 0;
            if (sign is null) sign = s;
            else if (sign != s) return false;
        }
        return true;
    }

    /// <summary>The picture's typical colour (per-channel median of a coarse sample).</summary>
    private static (byte, byte, byte) Background(RgbImage img)
    {
        var r = new List<byte>(); var g = new List<byte>(); var b = new List<byte>();
        int step = Math.Max(1, (int)Math.Sqrt(img.Width * (double)img.Height / 40000));
        for (int y = 0; y < img.Height; y += step)
            for (int x = 0; x < img.Width; x += step)
            {
                int i = (y * img.Width + x) * 3;
                r.Add(img.Pixels[i]); g.Add(img.Pixels[i + 1]); b.Add(img.Pixels[i + 2]);
            }
        r.Sort(); g.Sort(); b.Sort();
        return (r[r.Count / 2], g[g.Count / 2], b[b.Count / 2]);
    }
}
