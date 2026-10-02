using MOPAChroma.Atlas;
using MOPAChroma.Color;

namespace MOPAChroma.Scanning;

/// <summary>
/// One cell as read from a scan, with an outcome the user is asked to confirm.
/// <c>Spread</c> is the mean per-channel standard deviation inside the sampled area
/// (0..255); <c>DeltaEFromBare</c> is ΔE00 from the unengraved surface between squares.
/// </summary>
public sealed record CellReading(
    int Row, int Column, Srgb8 Rgb, Lab Lab, double Spread, double DeltaEFromBare,
    Outcome Suggested, string Why);

public sealed record ScanMeasurement(IReadOnlyList<CellReading> Cells, Lab Bare, int BareSamples)
{
    public CellReading At(int row, int col) => Cells.First(c => c.Row == row && c.Column == col);
}

/// <summary>
/// Reads every cell of a fitted grid. The centre 40 % of each square is sampled (clear of
/// edges, labels and slight misplacement), the per-channel median taken so specks and
/// scratches do not skew it, and the result converted to Lab.
///
/// The bare surface is measured from the gaps between squares - unengraved metal on the
/// same card, in the same scan - which gives an automatic "no mark" reference without
/// the user having to point at anything.
///
/// Sampling windows follow each cell's local pitch, so a photographed card (squares
/// smaller toward the far edge) is sampled over the same fraction of every square.
/// </summary>
public static class ScanMeasure
{
    /// <summary>Closer than this to bare metal (ΔE00) suggests "no mark".</summary>
    public const double NoMarkDeltaE = 2.5;
    /// <summary>Spread above this (0..255 units) suggests "uneven".</summary>
    public const double UnevenSpread = 14;

    public static ScanMeasurement Measure(RgbImage img, GridGeometry grid, double sampleFraction = 0.4)
    {
        var bare = MeasureBare(img, grid, out int bareCount);

        var cells = new List<CellReading>(grid.Rows * grid.Columns);
        for (int r = 0; r < grid.Rows; r++)
            for (int c = 0; c < grid.Columns; c++)
            {
                var p = grid.Centre(r, c);
                int half = Math.Max(1, (int)(grid.PitchAt(r, c) * sampleFraction / 2));
                var (rgb, spread) = MedianBox(img, (int)p.X, (int)p.Y, half);
                var lab = ColorConvert.ToLab(rgb);
                double dBare = DeltaE.Cie2000(lab, bare);

                var (outcome, why) =
                    dBare < NoMarkDeltaE ? (Outcome.NoMark, $"within ΔE00 {dBare:0.0} of bare metal")
                    : spread > UnevenSpread ? (Outcome.Uneven, $"uneven (spread {spread:0})")
                    : (Outcome.Good, "");
                cells.Add(new CellReading(r, c, rgb, lab, spread, dBare, outcome, why));
            }
        return new ScanMeasurement(cells, bare, bareCount);
    }

    /// <summary>Median of small patches in the gaps midway between horizontally and vertically adjacent cells.</summary>
    private static Lab MeasureBare(RgbImage img, GridGeometry g, out int count)
    {
        int half = Math.Max(1, (int)(g.MinPitch * 0.04));
        var rs = new List<byte>(); var gs = new List<byte>(); var bs = new List<byte>();
        int n = 0;
        for (int r = 0; r < g.Rows; r++)
            for (int c = 0; c < g.Columns; c++)
            {
                if (c + 1 < g.Columns) Add(g.CentreAt(r, c + 0.5));
                if (r + 1 < g.Rows) Add(g.CentreAt(r + 0.5, c));
            }
        count = n;
        if (rs.Count == 0) return new Lab(0, 0, 0);
        return ColorConvert.ToLab(new Srgb8(Median(rs), Median(gs), Median(bs)));

        void Add(PointD p)
        {
            int x = (int)p.X, y = (int)p.Y;
            if (x - half < 0 || y - half < 0 || x + half >= img.Width || y + half >= img.Height) return;
            n++;
            for (int yy = y - half; yy <= y + half; yy++)
                for (int xx = x - half; xx <= x + half; xx++)
                {
                    int i = (yy * img.Width + xx) * 3;
                    rs.Add(img.Pixels[i]); gs.Add(img.Pixels[i + 1]); bs.Add(img.Pixels[i + 2]);
                }
        }
    }

    private static (Srgb8 rgb, double spread) MedianBox(RgbImage img, int cx, int cy, int half)
    {
        int x0 = Math.Clamp(cx - half, 0, img.Width - 1), x1 = Math.Clamp(cx + half, 0, img.Width - 1);
        int y0 = Math.Clamp(cy - half, 0, img.Height - 1), y1 = Math.Clamp(cy + half, 0, img.Height - 1);
        int n = (x1 - x0 + 1) * (y1 - y0 + 1);
        var r = new byte[n]; var g = new byte[n]; var b = new byte[n];
        int k = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++, k++)
            {
                int i = (y * img.Width + x) * 3;
                r[k] = img.Pixels[i]; g[k] = img.Pixels[i + 1]; b[k] = img.Pixels[i + 2];
            }
        double spread = (StdDev(r) + StdDev(g) + StdDev(b)) / 3;
        return (new Srgb8(Median(r), Median(g), Median(b)), spread);
    }

    private static byte Median(IReadOnlyList<byte> v)
    {
        var hist = new int[256];
        foreach (var x in v) hist[x]++;
        int target = v.Count / 2, acc = 0;
        for (int i = 0; i < 256; i++)
        {
            acc += hist[i];
            if (acc > target) return (byte)i;
        }
        return 255;
    }

    private static double StdDev(byte[] v)
    {
        double mean = 0;
        foreach (var x in v) mean += x;
        mean /= v.Length;
        double ss = 0;
        foreach (var x in v) ss += (x - mean) * (x - mean);
        return Math.Sqrt(ss / v.Length);
    }
}
