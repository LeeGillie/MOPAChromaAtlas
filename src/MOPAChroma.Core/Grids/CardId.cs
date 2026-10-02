using MOPAChroma.Scanning;

namespace MOPAChroma.Grids;

/// <summary>
/// The grid number engraved on every Atlas card, so any later picture of it (a flatbed scan
/// of a whole sheet, a USB-camera photo) says which grid it is.
/// <para>
/// A row of 18 dot positions in the margin between the two top corner crosses: an always-on
/// dot at each end, 12 bits of grid number (1-4095, most significant first) and a 4-bit
/// check (CRC-4). A dot is a small filled square engraved with the grid's strongest
/// settings; no dot is a 0. The reader finds the grid by its crosses first, so it knows
/// exactly where each dot position is; the two end dots show what a dot looks like on that
/// card, and the check rejects a misread.
/// </para>
/// </summary>
public static class CardId
{
    public const int Positions = 18;
    public const int MaxNumber = 4095;

    /// <summary>The 18 dot positions for a grid number: true = dot.</summary>
    public static bool[] Encode(int number)
    {
        if (number < 1 || number > MaxNumber) throw new ArgumentOutOfRangeException(nameof(number));
        var bits = new bool[Positions];
        bits[0] = bits[^1] = true;
        for (int i = 0; i < 12; i++) bits[1 + i] = ((number >> (11 - i)) & 1) == 1;
        int crc = Crc4(number);
        for (int i = 0; i < 4; i++) bits[13 + i] = ((crc >> (3 - i)) & 1) == 1;
        return bits;
    }

    /// <summary>The grid number, or null when the end dots are missing or the check fails.</summary>
    public static int? Decode(IReadOnlyList<bool> bits)
    {
        if (bits.Count != Positions || !bits[0] || !bits[^1]) return null;
        int n = 0, crc = 0;
        for (int i = 0; i < 12; i++) n = (n << 1) | (bits[1 + i] ? 1 : 0);
        for (int i = 0; i < 4; i++) crc = (crc << 1) | (bits[13 + i] ? 1 : 0);
        return n >= 1 && crc == Crc4(n) ? n : null;
    }

    /// <summary>CRC-4 (polynomial x⁴ + x + 1) of a 12-bit value.</summary>
    private static int Crc4(int value)
    {
        int reg = 0;
        for (int i = 11; i >= 0; i--)
        {
            int bit = (value >> i) & 1;
            int top = (reg >> 3) & 1;
            reg = (reg << 1) & 0xF;
            if ((top ^ bit) == 1) reg ^= 0x3;
        }
        return reg;
    }

    /// <summary>
    /// Where the dot positions sit, relative to the top-left cell's centre, in cell pitches
    /// (columns right, rows down): on the line of the top corner crosses, between them.
    /// Null when the grid is too narrow for a readable code (fewer than 2 columns, or dots
    /// that would be under 0.45 mm apart).
    /// </summary>
    public static Layout? For(GridLayout l)
    {
        if (l.Columns < 2) return null;
        double pitch = l.CellSizeMm + l.GapMm;
        double size = CornerMarks.SizeMm(l), half = size / 2, clear = size / 2;
        double span = (l.Columns - 1) * pitch - 2 * (half + clear);
        double bit = Math.Min(1.0, span / Positions);
        if (bit < 0.45) return null;
        double startMm = half + clear + bit / 2;
        return new Layout(-CornerMarks.OffsetPitches(l), startMm / pitch, bit / pitch, Math.Min(0.6 * bit, size), bit);
    }

    /// <summary>
    /// The dots a grid engraves for its number, in machine mm: the centre and side of each
    /// filled square. Empty when the grid has no number or is too narrow for one.
    /// <paramref name="originX"/>, <paramref name="originY"/> are the top-left corner of the
    /// first cell. The job (GcodeExport) and the Send to laser preview both use this, so they agree.
    /// </summary>
    public static IReadOnlyList<(double X, double Y, double SizeMm)> DotsMm(GridLayout l, int number, double originX, double originY)
    {
        if (number is < 1 or > MaxNumber || For(l) is not { } id) return [];
        double pitch = l.CellSizeMm + l.GapMm;
        double cx0 = originX + l.CellSizeMm / 2, y = originY + l.CellSizeMm / 2 - CornerMarks.OffsetMm(l);
        var bits = Encode(number);
        var dots = new List<(double, double, double)>();
        for (int i = 0; i < bits.Length; i++)
            if (bits[i]) dots.Add((cx0 + (id.FirstColumn + i * id.Step) * pitch, y, id.DotMm));
        return dots;
    }

    /// <param name="Row">Row of the dot line, in cell pitches from the top-left cell centre (negative: above).</param>
    /// <param name="FirstColumn">Column of the first dot, in cell pitches.</param>
    /// <param name="Step">Columns between dots, in cell pitches.</param>
    /// <param name="DotMm">Side of each dot, mm.</param>
    /// <param name="StepMm">Distance between dots, mm.</param>
    public sealed record Layout(double Row, double FirstColumn, double Step, double DotMm, double StepMm);

    /// <summary>
    /// Reads the grid number from a picture in which the grid has been placed. Samples each
    /// dot position, compares with the two end dots, and checks the result. Null when there
    /// is no readable code (an older card, too little contrast, or a misread).
    /// </summary>
    public static int? Read(RgbImage img, GridGeometry g, GridLayout l)
    {
        if (For(l) is not { } at) return null;
        var level = new double[Positions];
        for (int i = 0; i < Positions; i++)
        {
            var p = g.CentreAt(at.Row, at.FirstColumn + i * at.Step);
            double pxPerPitch = g.PitchAt(0, 0);
            double r = Math.Max(1, 0.18 * at.Step * pxPerPitch);
            level[i] = Mean(img, p, r);
        }
        double dot = (level[0] + level[^1]) / 2;
        // Bare metal: the inner position least like the end dots.
        double bare = level.Skip(1).Take(Positions - 2).MaxBy(v => Math.Abs(v - dot));
        if (Math.Abs(dot - bare) < 12) return null;
        var bits = level.Select(v => Math.Abs(v - dot) < Math.Abs(v - bare)).ToArray();
        return Decode(bits);
    }

    private static double Mean(RgbImage img, PointD c, double r)
    {
        int x0 = (int)Math.Floor(c.X - r), x1 = (int)Math.Ceiling(c.X + r), y0 = (int)Math.Floor(c.Y - r), y1 = (int)Math.Ceiling(c.Y + r);
        double sum = 0;
        int n = 0;
        for (int y = Math.Max(0, y0); y <= Math.Min(img.Height - 1, y1); y++)
            for (int x = Math.Max(0, x0); x <= Math.Min(img.Width - 1, x1); x++)
            {
                int i = (y * img.Width + x) * 3;
                sum += 0.299 * img.Pixels[i] + 0.587 * img.Pixels[i + 1] + 0.114 * img.Pixels[i + 2];
                n++;
            }
        return n == 0 ? 0 : sum / n;
    }
}
