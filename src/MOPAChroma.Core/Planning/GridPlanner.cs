using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Laser;

namespace MOPAChroma.Planning;

/// <summary>A setting the planner wants engraved, and why.</summary>
public sealed record Proposal(MopaSettings Settings, Physics Physics, string Reason, double Score);

/// <summary>
/// Chooses which settings go on the next test grid.
///
/// Seed grid: spread the cells as far apart as possible in physics space
/// (farthest-point sampling), so the first coupon surveys the whole map instead of one
/// corner of it, and physically equivalent settings are never both used.
///
/// Refinement grids: look at what came back and spend cells only where they can
/// teach something - between neighbours whose colours differ (a colour boundary runs
/// through there), in regions nobody has tested, and away from zones already shown
/// to be no-mark or damaged. Where every neighbour is the same colour, the region is
/// already mapped and gets no cells: those are the "meaningless" cells of a
/// conventional sweep.
/// </summary>
public sealed class GridPlanner
{
    private readonly ParameterSpace _space;
    private readonly SourceProfile _source;
    private readonly LensProfile _lens;
    private readonly Lazy<CandidatePool> _pool;

    /// <summary>
    /// Candidates closer than this to anything already engraved (in the planner's normalised
    /// physics space, where the whole explored range is about 1 on each axis) are not proposed:
    /// they would be physically almost the same experiment again.
    /// </summary>
    public const double MinNewDistance = 0.02;

    /// <summary>
    /// How strongly refinement chases colours the atlas does not have yet (cells near rare
    /// hues, and settings hotter than any good cell so far). 0 turns it off.
    /// </summary>
    public double ColourSeeking { get; init; } = 1.0;

    /// <summary>Colours paler than this (CIELAB chroma) count as greys and silvers, not hues.</summary>
    private const double ColourfulChroma = 10;

    private static int HueBin(Lab c) => Math.Clamp((int)(c.HueDegrees / 30.0), 0, 11);

    /// <summary>Neighbours consulted for each candidate during refinement.</summary>
    public int Neighbours { get; init; } = 5;

    public GridPlanner(ParameterSpace space, SourceProfile source, LensProfile lens, int poolSize = 4000)
    {
        _space = space;
        _source = source;
        _lens = lens;
        _pool = new Lazy<CandidatePool>(() => new CandidatePool(_space, _source, _lens, poolSize));
    }

    /// <summary>
    /// Plan the first grid for a material. Any settings already engraved are treated
    /// as occupied, so a seed can be topped up without repeating itself.
    /// </summary>
    public IReadOnlyList<Proposal> Seed(int count, IEnumerable<MopaSettings>? alreadyTested = null, double maxArealEnergy = double.PositiveInfinity)
    {
        var pool = _pool.Value;
        var occupied = (alreadyTested ?? []).Select(pool.PositionOf).ToList();
        var items = pool.Items.Where(c => c.Physics.ArealEnergyJPerMm2 <= maxArealEnergy).ToList();
        if (items.Count == 0 || count <= 0) return [];

        // Distance from each candidate to its nearest occupied point.
        var nearest = new double[items.Count];
        for (int i = 0; i < items.Count; i++)
            nearest[i] = occupied.Count == 0 ? double.MaxValue : occupied.Min(o => CandidatePool.Distance(items[i].Position, o));

        var picks = new List<Proposal>();
        var used = new bool[items.Count];

        // With nothing occupied, start from the candidate nearest the centre of the space.
        if (occupied.Count == 0)
        {
            var centre = Enumerable.Repeat(0.5, items[0].Position.Length).ToArray();
            int first = ArgMin(items.Count, i => CandidatePool.Distance(items[i].Position, centre));
            Take(first, "Centre of the explored space");
        }

        while (picks.Count < count)
        {
            int best = -1;
            double bestD = -1;
            for (int i = 0; i < items.Count; i++)
                if (!used[i] && nearest[i] > bestD) { bestD = nearest[i]; best = i; }
            if (best < 0) break;
            Take(best, $"Farthest from anything tested (distance {bestD:0.00})");
        }

        return picks;

        void Take(int index, string reason)
        {
            used[index] = true;
            var c = items[index];
            picks.Add(new Proposal(c.Settings, c.Physics, reason, nearest[index] == double.MaxValue ? 1 : nearest[index]));
            for (int i = 0; i < items.Count; i++)
                nearest[i] = Math.Min(nearest[i], CandidatePool.Distance(items[i].Position, c.Position));
        }
    }

    /// <summary>
    /// Plan a refinement grid from measured samples of ONE material, source and lens.
    /// Falls back to <see cref="Seed"/> while there are too few results to reason about.
    /// </summary>
    public IReadOnlyList<Proposal> Refine(int count, IReadOnlyList<Sample> samples)
    {
        var measured = samples.Where(s => s.Outcome != Outcome.Pending).ToList();
        // Hard heat ceiling from the results so far (HeatLimits): never beyond it, whatever scores well.
        double ceiling = HeatLimits.Ceiling(samples, _source, _lens);
        if (measured.Count < Neighbours + 1)
            return Seed(count, samples.Select(s => s.Settings), ceiling);

        var pool = _pool.Value;
        var known = measured.Select(s => (sample: s, pos: pool.PositionOf(s.Settings))).ToList();
        var allTested = samples.Select(s => pool.PositionOf(s.Settings)).ToList();
        var testedSettings = samples.Select(s => s.Settings).ToList();

        var noMarks = measured.Where(s => s.Outcome == Outcome.NoMark)
            .Select(s => Physics.From(s.Settings, _source, _lens)).ToList();
        var damaged = measured.Where(s => s.Outcome == Outcome.Damaged)
            .Select(s => Physics.From(s.Settings, _source, _lens)).ToList();

        // Colour seeking. How common each hue already is (12 bins of 30°, coloured cells only),
        // so cells near a rare colour (one blue among thirty golds) are worth more; and how
        // much heat the good cells have taken so far, because thicker oxide (purples, blues
        // and beyond) needs more heat than the first golds and browns.
        var hueCount = new int[12];
        foreach (var smp in measured.Where(x => x.Outcome == Outcome.Good && x.Color is { Chroma: >= ColourfulChroma }))
            hueCount[HueBin(smp.Color!.Value)]++;
        var goodHeat = measured.Where(x => x.Outcome == Outcome.Good)
            .Select(x => Physics.From(x.Settings, _source, _lens).ArealEnergyJPerMm2).Where(double.IsFinite).ToList();
        double hottestGood = goodHeat.Count > 0 ? goodHeat.Max() : double.NaN;

        var scored = new List<(Candidate c, double score, string reason)>();
        foreach (var c in pool.Items)
        {
            if (!(c.Physics.ArealEnergyJPerMm2 <= ceiling)) continue;
            double dTested = allTested.Min(t => CandidatePool.Distance(c.Position, t));
            // Already engraved, or physically almost the same as something that was: no new knowledge.
            if (dTested < MinNewDistance) continue;
            if (testedSettings.Any(t => Repeatability.SameSettings(t, c.Settings))) continue;

            // Dominance pruning. A candidate that delivers less energy per area AND per
            // pulse than a cell that left no mark will not mark either; one that
            // delivers more of both than a cell that was damaged will be damaged too.
            // These are certain enough to skip outright - no cell is spent proving them.
            if (noMarks.Any(n => c.Physics.ArealEnergyJPerMm2 <= n.ArealEnergyJPerMm2 && c.Physics.PulseEnergyMJ <= n.PulseEnergyMJ))
                continue;
            if (damaged.Any(d => c.Physics.ArealEnergyJPerMm2 >= d.ArealEnergyJPerMm2 && c.Physics.PulseEnergyMJ >= d.PulseEnergyMJ))
                continue;
            // (Hotter per area than a damaged cell but with smaller pulses - many passes over
            // close lines - is excluded by the ceiling above, which counts every pass.)

            var near = known
                .Select(k => (k.sample, d: CandidatePool.Distance(c.Position, k.pos)))
                .OrderBy(x => x.d)
                .Take(Neighbours)
                .ToList();

            // Distance-weighted estimate of how likely this spot is dead ground.
            double wSum = 0, wDead = 0;
            foreach (var (s, d) in near)
            {
                double w = 1.0 / Math.Pow(d + 1e-3, 2);
                wSum += w;
                if (s.Outcome is Outcome.NoMark or Outcome.Damaged) wDead += w;
            }
            double pDead = wSum > 0 ? wDead / wSum : 0;

            // Peaks at 1 where good and dead neighbours balance: an edge of the usable
            // region runs through here, and locating it is worth a cell or two.
            double edge = 4 * pDead * (1 - pDead);

            var colours = near.Where(n => n.sample.HasColor).Select(n => n.sample.Color!.Value).ToList();
            double spread = MaxPairwiseDeltaE(colours);

            // Rare colour nearby: the rarest hue among the coloured neighbours, 1 when it is
            // the only one of its kind in the atlas, falling off as that hue gets common.
            double rarity = 0;
            Lab? rareColour = null;
            foreach (var (smp, _) in near)
            {
                if (smp.Outcome != Outcome.Good || smp.Color is not { Chroma: >= ColourfulChroma } col) continue;
                double r = 1.0 / Math.Max(1, hueCount[HueBin(col)]);
                if (r > rarity) { rarity = r; rareColour = col; }
            }
            // More heat than any good cell so far (but not dominated by a damaged one, above).
            // Rises from 0.8× to 1.2× the hottest good cell and stays there; the ceiling (at most
            // 1.5×) is the hard stop.
            double heat = double.IsFinite(hottestGood) && hottestGood > 0
                ? Math.Clamp((c.Physics.ArealEnergyJPerMm2 / hottestGood - 0.8) / 0.4, 0, 1)
                : 0;

            // Novelty (dTested) times interest: colour changing nearby, an edge, a rare colour,
            // or new heat (where new colours come from).
            double score = dTested * (1.0 + spread / DeltaE.JustNoticeable + 3.0 * edge + ColourSeeking * (2.0 * rarity + 1.5 * heat))
                           * (1.0 - 0.9 * pDead * pDead);

            string reason = ColourSeeking > 0 && rarity >= 0.5 && rareColour is { } rc ? $"Near a rare colour ({ColorNames.Name(rc).ToLowerInvariant()}): look for more like it"
                : ColourSeeking > 0 && heat >= 0.5 && edge <= 0.5 ? "More heat than any good cell so far: thicker oxide, new colours"
                : edge > 0.5 ? $"Edge of the usable region ({pDead:P0} likely no-mark/damaged)"
                : spread >= DeltaE.JustNoticeable ? $"Colour changes nearby (neighbours differ by ΔE00 {spread:0.0})"
                : $"Untested region (distance {dTested:0.00})";

            scored.Add((c, score, reason));
        }

        // Greedy pick with a spacing penalty so one hot spot does not take every cell.
        var chosen = new List<Proposal>();
        var chosenPos = new List<double[]>();
        double spacing = TypicalSpacing(allTested);

        while (chosen.Count < count && scored.Count > 0)
        {
            int best = -1;
            double bestScore = double.MinValue;
            for (int i = 0; i < scored.Count; i++)
            {
                double s = scored[i].score;
                if (chosenPos.Count > 0)
                {
                    double d = chosenPos.Min(p => CandidatePool.Distance(scored[i].c.Position, p));
                    s *= Math.Min(1.0, d / spacing);
                }
                if (s > bestScore) { bestScore = s; best = i; }
            }
            if (best < 0) break;
            var pick = scored[best];
            chosen.Add(new Proposal(pick.c.Settings, pick.c.Physics, pick.reason, bestScore));
            chosenPos.Add(pick.c.Position);
            scored.RemoveAt(best);
        }

        return chosen;
    }

    private static double MaxPairwiseDeltaE(IReadOnlyList<Lab> colours)
    {
        double max = 0;
        for (int i = 0; i < colours.Count; i++)
            for (int j = i + 1; j < colours.Count; j++)
                max = Math.Max(max, DeltaE.Cie2000(colours[i], colours[j]));
        return max;
    }

    /// <summary>Median nearest-neighbour distance among tested points: the current mapping resolution.</summary>
    private static double TypicalSpacing(IReadOnlyList<double[]> pts)
    {
        if (pts.Count < 2) return 0.1;
        var nn = pts.Select((p, i) => pts.Where((_, j) => j != i).Min(q => CandidatePool.Distance(p, q)));
        return Math.Max(1e-3, Median(nn));
    }

    private static double Median(IEnumerable<double> xs)
    {
        var a = xs.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2.0;
    }

    private static int ArgMin(int n, Func<int, double> f)
    {
        int best = 0;
        double bv = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            double v = f(i);
            if (v < bv) { bv = v; best = i; }
        }
        return best;
    }
}
