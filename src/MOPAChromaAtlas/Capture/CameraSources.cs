using System.Runtime.InteropServices;
using FlashCap;
using FlashCap.Devices;

namespace MOPAChromaAtlas.Capture;

/// <summary>
/// Finds cameras through each of the platform's camera interfaces separately, each with a
/// time limit, so one interface that hangs does not stop the others being listed. On
/// Windows, Media Foundation is asked as well as DirectShow, for cameras that only appear there.
/// </summary>
public static class CameraSources
{
    public static IEnumerable<(string Name, Func<CaptureDevices> Make)> Interfaces()
    {
        // Each interface is only constructed on its own platform; the analyser cannot see through the lambdas.
#pragma warning disable CA1416
        if (OperatingSystem.IsWindows())
        {
            yield return ("DirectShow", () => new DirectShowDevices());
            yield return ("Media Foundation", () => new MediaFoundationDevices());
        }
        else if (OperatingSystem.IsLinux()) yield return ("V4L2", () => new V4L2Devices());
        else if (OperatingSystem.IsMacOS()) yield return ("AVFoundation", () => new AVFoundationDevices());
#pragma warning restore CA1416
    }

    /// <summary>What a search found: the cameras, and any interface that failed or did not answer.</summary>
    public sealed record Found(IReadOnlyList<CaptureDeviceDescriptor> Devices, IReadOnlyList<string> Problems);

    public static async Task<Found> FindAsync(TimeSpan perInterface)
    {
        var devices = new List<CaptureDeviceDescriptor>();
        var problems = new List<string>();
        foreach (var (name, make) in Interfaces())
        {
            var t = Task.Run(() => make().EnumerateDescriptors().ToArray());
            if (await Task.WhenAny(t, Task.Delay(perInterface)) != t) { problems.Add($"{name} did not answer"); continue; }
            if (t.IsFaulted) { problems.Add($"{name}: {t.Exception!.GetBaseException().Message}"); continue; }
            foreach (var d in t.Result)
                // The same camera is usually listed by both interfaces; keep the first (DirectShow, which offers more formats).
                if (!devices.Any(x => x.Name == d.Name)) devices.Add(d);
        }
        return new Found(devices, problems);
    }

    /// <summary>True when this Windows session is a Remote Desktop session (cameras then come through the RDP client).</summary>
    public static bool IsRemoteSession => OperatingSystem.IsWindows() && GetSystemMetrics(0x1000) != 0;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
