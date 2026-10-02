namespace LumosUltra.Client.Models;

/// <summary>
/// The integer code returned by <c>GET /process/status</c>. Values are CONFIRMED by
/// direct observation of a Lumos Ultra's own MakeIt application log (this project's
/// authors watched <c>status:4</c> while a job sat armed and waiting for the physical
/// start button, and <c>status:6</c> during a framing/boundary preview pass) and
/// independently corroborated by the community project marcoreps/lumos_RE. The full
/// Idle(4) -&gt; Preview(6) -&gt; Idle(4) cycle was additionally reproduced end-to-end
/// through this library itself (not just observed from MakeIt's own traffic) via
/// <see cref="LumosUltraClient.StartFramingPreviewAsync"/> and
/// <see cref="LumosUltraClient.StopFramingPreviewAsync"/> against real hardware. See
/// PROVENANCE.md.
/// </summary>
public enum JobStatus
{
    /// <summary>No confirmed meaning yet observed for this code.</summary>
    Unknown = -1,

    /// <summary>The controller is actively running a job.</summary>
    Busy = 2,

    /// <summary>A job has finished (or is loaded/armed); exact boundary with <see cref="Idle"/>
    /// is not fully confirmed - marcoreps/lumos_RE notes this value's semantics as unresolved.</summary>
    FinishedOrArmed = 3,

    /// <summary>Idle and waiting - this is the state observed while a job sits armed,
    /// waiting for the physical start button to be pressed.</summary>
    Idle = 4,

    /// <summary>Tracing the job's boundary as a low-power framing/preview pass.</summary>
    Preview = 6,
}

internal static class JobStatusExtensions
{
    public static JobStatus ToJobStatus(this int code) => code switch
    {
        2 => JobStatus.Busy,
        3 => JobStatus.FinishedOrArmed,
        4 => JobStatus.Idle,
        6 => JobStatus.Preview,
        _ => JobStatus.Unknown,
    };
}
