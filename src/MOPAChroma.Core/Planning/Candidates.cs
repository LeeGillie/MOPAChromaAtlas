using MOPAChroma.Laser;

namespace MOPAChroma.Planning;

/// <summary>A proposed setting with its position in normalised physics space.</summary>
internal sealed record Candidate(MopaSettings Settings, Physics Physics, double[] Position);

/// <summary>
/// Generates a dense, even spread of valid settings and places them in normalised
/// physics space, where each planning axis runs 0..1 over the pool. The planners
/// then choose from this pool; they never invent settings of their own.
/// </summary>
internal sealed class CandidatePool
{
    public IReadOnlyList<Candidate> Items { get; }
    private readonly double[] _min, _max;
    private readonly SourceProfile _source;
    private readonly LensProfile _lens;

    public CandidatePool(ParameterSpace space, SourceProfile source, LensProfile lens, int size = 4000)
    {
        _source = source;
        _lens = lens;

        var raw = new List<(MopaSettings s, Physics p, double[] v)>(size);
        var seen = new HashSet<MopaSettings>();
        Span<double> u = stackalloc double[6];

        // Attempts are capped so an over-constrained space cannot loop forever.
        for (int i = 1; raw.Count < size && i < size * 20; i++)
        {
            Halton.Point(i + 20, u); // skip the first few, which cluster in a corner
            var s = space.At(u);
            if (!seen.Add(s)) continue; // snapping makes exact duplicates common
            var p = Physics.From(s, source, lens);
            if (!space.IsValid(s, p)) continue;
            raw.Add((s, p, p.PlanningVector(s.PulseWidthNs)));
        }

        int dims = raw.Count > 0 ? raw[0].v.Length : 4;
        _min = Enumerable.Repeat(double.MaxValue, dims).ToArray();
        _max = Enumerable.Repeat(double.MinValue, dims).ToArray();
        foreach (var (_, _, v) in raw)
            for (int d = 0; d < dims; d++)
            {
                _min[d] = Math.Min(_min[d], v[d]);
                _max[d] = Math.Max(_max[d], v[d]);
            }

        Items = raw.Select(r => new Candidate(r.s, r.p, Normalise(r.v))).ToList();
    }

    /// <summary>Position of any setting (e.g. an already engraved one) in this pool's space.</summary>
    public double[] PositionOf(MopaSettings s) =>
        Normalise(Physics.From(s, _source, _lens).PlanningVector(s.PulseWidthNs));

    private double[] Normalise(double[] v)
    {
        var n = new double[v.Length];
        for (int d = 0; d < v.Length; d++)
        {
            double span = _max[d] - _min[d];
            n[d] = span > 1e-12 ? (v[d] - _min[d]) / span : 0.5;
        }
        return n;
    }

    public static double Distance(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            sum += d * d;
        }
        return Math.Sqrt(sum);
    }
}

/// <summary>Halton low-discrepancy sequence: evenly spread points without a regular lattice.</summary>
internal static class Halton
{
    private static readonly int[] Primes = [2, 3, 5, 7, 11, 13, 17, 19];

    public static void Point(int index, Span<double> into)
    {
        for (int d = 0; d < into.Length; d++)
            into[d] = RadicalInverse(index, Primes[d]);
    }

    public static double RadicalInverse(int i, int b)
    {
        double f = 1, r = 0;
        while (i > 0)
        {
            f /= b;
            r += f * (i % b);
            i /= b;
        }
        return r;
    }
}
