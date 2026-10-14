using LumosUltra.Client;
using LumosUltra.Jobs;
namespace MOPAChromaAtlas.Capture;

/// <summary>
/// The only way Atlas sends a job to the laser. Every job is checked by <see cref="JobAudit"/>
/// first (energy per square millimetre, read from the G-code itself) and refused if any spot
/// would get more than the safe limit, whatever produced the job. The power used for the check
/// is the highest any known source is rated for, so a wrong laser choice in the window can
/// only make the check stricter, never looser.
/// </summary>
public static class SafeSend
{
    public static double CheckPowerW(SourceProfile? chosen) =>
        Math.Max(chosen?.RatedPowerW ?? 0, SourceProfile.Presets.Max(p => p.RatedPowerW));

    /// <summary>Audits, then uploads and arms. Throws <see cref="UnsafeJobException"/> (nothing sent) when the job fails the check.</summary>
    public static async Task<JobAudit.Result> UploadAndArmAsync(LumosUltraClient client, string gcode, SourceProfile? source, CancellationToken ct)
    {
        var audit = JobAudit.Check(gcode, CheckPowerW(source));
        if (!audit.Ok) throw new UnsafeJobException(audit);
        await client.UploadAndArmJobAsync(gcode, ct: ct);
        return audit;
    }
}

/// <summary>A job refused by the heat check. Nothing was sent.</summary>
public sealed class UnsafeJobException(JobAudit.Result audit) : Exception(audit.Message)
{
    public JobAudit.Result Audit { get; } = audit;
}
