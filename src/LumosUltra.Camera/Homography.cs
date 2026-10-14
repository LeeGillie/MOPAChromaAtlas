namespace LumosUltra.Camera;

/// <summary>
/// A plane-to-plane perspective map, x' = (h0 x + h1 y + h2) / (h6 x + h7 y + 1), and the
/// same for y' with h3..h5. A flatbed scan only ever needs the affine part (h6 = h7 = 0);
/// a photo taken at an angle needs the full eight parameters, because squares further
/// from the camera come out smaller.
/// </summary>
public sealed class Homography
{
    private readonly double[] _h; // 9 values, row-major, h[8] == 1

    public Homography(double[] h)
    {
        if (h.Length != 9) throw new ArgumentException("A homography has nine coefficients.", nameof(h));
        double s = h[8];
        if (Math.Abs(s) < 1e-15) throw new ArgumentException("Degenerate homography.", nameof(h));
        _h = h.Select(v => v / s).ToArray();
    }

    public IReadOnlyList<double> Coefficients => _h;

    public static Homography Identity => new([1, 0, 0, 0, 1, 0, 0, 0, 1]);

    /// <summary>The affine map p = origin + x·u + y·v.</summary>
    public static Homography Affine(PointD origin, PointD u, PointD v) =>
        new([u.X, v.X, origin.X, u.Y, v.Y, origin.Y, 0, 0, 1]);

    public PointD Map(double x, double y)
    {
        double w = _h[6] * x + _h[7] * y + 1;
        return new PointD((_h[0] * x + _h[1] * y + _h[2]) / w, (_h[3] * x + _h[4] * y + _h[5]) / w);
    }

    public PointD Map(PointD p) => Map(p.X, p.Y);

    /// <summary>True when the map has no perspective term (a scan, not a photo).</summary>
    public bool IsAffine => Math.Abs(_h[6]) < 1e-12 && Math.Abs(_h[7]) < 1e-12;

    /// <summary>Applies <paramref name="then"/> after this map.</summary>
    public Homography Then(Homography then) => new(Mul(then._h, _h));

    /// <summary>Scales the output, e.g. from a reduced working copy back to full resolution.</summary>
    public Homography ScaleOutput(double k) => Then(new Homography([k, 0, 0, 0, k, 0, 0, 0, 1]));

    public Homography? Inverse()
    {
        var a = _h;
        double c00 = a[4] * a[8] - a[5] * a[7], c01 = a[5] * a[6] - a[3] * a[8], c02 = a[3] * a[7] - a[4] * a[6];
        double det = a[0] * c00 + a[1] * c01 + a[2] * c02;
        if (Math.Abs(det) < 1e-15) return null;
        double[] inv =
        [
            c00, a[2] * a[7] - a[1] * a[8], a[1] * a[5] - a[2] * a[4],
            c01, a[0] * a[8] - a[2] * a[6], a[2] * a[3] - a[0] * a[5],
            c02, a[1] * a[6] - a[0] * a[7], a[0] * a[4] - a[1] * a[3],
        ];
        if (Math.Abs(inv[8]) < 1e-15) return null;
        return new Homography(inv);
    }

    private static double[] Mul(double[] a, double[] b)
    {
        var r = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return r;
    }

    /// <summary>
    /// Least-squares fit from source to target points. With <paramref name="affineOnly"/>
    /// the perspective terms are held at zero (needs 3 points, otherwise 4). Points are
    /// normalised first (Hartley) so pixel-sized numbers do not wreck the conditioning.
    /// Returns null when the points cannot determine a map (too few, or all in a line).
    /// </summary>
    public static Homography? Fit(IReadOnlyList<PointD> src, IReadOnlyList<PointD> dst, bool affineOnly = false)
    {
        int n = src.Count;
        if (n != dst.Count) throw new ArgumentException("Point lists differ in length.");
        if (n < (affineOnly ? 3 : 4)) return null;

        var (ts, ns) = Normaliser(src);
        var (td, nd) = Normaliser(dst);
        int k = affineOnly ? 6 : 8;
        var ata = new double[k, k];
        var atb = new double[k];
        var row = new double[k];
        for (int i = 0; i < n; i++)
        {
            double x = ns[i].X, y = ns[i].Y, u = nd[i].X, v = nd[i].Y;
            // u·(h6 x + h7 y + 1) = h0 x + h1 y + h2  →  h0 x + h1 y + h2 − h6 x u − h7 y u = u
            Array.Clear(row);
            row[0] = x; row[1] = y; row[2] = 1;
            if (!affineOnly) { row[6] = -x * u; row[7] = -y * u; }
            Accumulate(ata, atb, row, u);
            Array.Clear(row);
            row[3] = x; row[4] = y; row[5] = 1;
            if (!affineOnly) { row[6] = -x * v; row[7] = -y * v; }
            Accumulate(ata, atb, row, v);
        }
        var sol = Solve(ata, atb);
        if (sol is null) return null;
        double[] h = [sol[0], sol[1], sol[2], sol[3], sol[4], sol[5], affineOnly ? 0 : sol[6], affineOnly ? 0 : sol[7], 1];
        // Undo the normalisation: H = Td⁻¹ · Hn · Ts.
        var hn = new Homography(h);
        var tdInv = td.Inverse();
        if (tdInv is null) return null;
        try { return ts.Then(hn).Then(tdInv); }
        catch (ArgumentException) { return null; }
    }

    private static void Accumulate(double[,] ata, double[] atb, double[] row, double rhs)
    {
        int k = row.Length;
        for (int i = 0; i < k; i++)
        {
            if (row[i] == 0) continue;
            atb[i] += row[i] * rhs;
            for (int j = 0; j < k; j++) ata[i, j] += row[i] * row[j];
        }
    }

    private static (Homography t, PointD[] pts) Normaliser(IReadOnlyList<PointD> p)
    {
        double mx = p.Average(q => q.X), my = p.Average(q => q.Y);
        double d = p.Average(q => Math.Sqrt((q.X - mx) * (q.X - mx) + (q.Y - my) * (q.Y - my)));
        double s = d > 1e-12 ? Math.Sqrt(2) / d : 1;
        var t = new Homography([s, 0, -s * mx, 0, s, -s * my, 0, 0, 1]);
        return (t, p.Select(q => new PointD((q.X - mx) * s, (q.Y - my) * s)).ToArray());
    }

    /// <summary>Gaussian elimination with partial pivoting; null if singular.</summary>
    private static double[]? Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
            if (Math.Abs(m[piv, c]) < 1e-12) return null;
            if (piv != c)
            {
                for (int j = 0; j < n; j++) (m[c, j], m[piv, j]) = (m[piv, j], m[c, j]);
                (x[c], x[piv]) = (x[piv], x[c]);
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = m[r, c] / m[c, c];
                if (f == 0) continue;
                for (int j = c; j < n; j++) m[r, j] -= f * m[c, j];
                x[r] -= f * x[c];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            double s = x[r];
            for (int j = r + 1; j < n; j++) s -= m[r, j] * x[j];
            x[r] = s / m[r, r];
        }
        return x;
    }
}
