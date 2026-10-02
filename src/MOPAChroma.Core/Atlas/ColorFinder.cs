using MOPAChroma.Color;

namespace MOPAChroma.Atlas;

public sealed record ColorMatch(Sample Sample, double DeltaE00, RepeatCheck Repeat);

/// <summary>
/// "What settings give me this colour?" Answers only from measured samples - it never
/// reports a prediction as if it had been engraved. Several matches are returned on
/// purpose: different settings reaching the same colour differ in speed, evenness and
/// repeatability, and the user chooses between them.
///
/// Verified colours rank first: an unverified match has to be <see cref="UnverifiedPenalty"/>
/// ΔE00 closer to beat a verified one, and a match whose repeat came out differently
/// <see cref="DiffersPenalty"/> closer. The reported ΔE00 is always the true one.
/// </summary>
public static class ColorFinder
{
    public const double UnverifiedPenalty = 2.0;
    public const double DiffersPenalty = 5.0;

    public static IReadOnlyList<ColorMatch> Find(IEnumerable<Sample> samples, Lab target, int max = 10)
    {
        var all = samples as IReadOnlyCollection<Sample> ?? samples.ToList();
        return all
            .Where(s => s.HasColor)
            .Select(s => new ColorMatch(s, DeltaE.Cie2000(target, s.Color!.Value), Repeatability.Check(s, all)))
            .OrderBy(m => m.DeltaE00 + m.Repeat.Status switch
            {
                RepeatStatus.Verified => 0,
                RepeatStatus.RepeatDiffers => DiffersPenalty,
                _ => UnverifiedPenalty,
            })
            .ThenBy(m => m.Sample.Outcome == Outcome.Good ? 0 : 1)
            .Take(max)
            .ToList();
    }
}
