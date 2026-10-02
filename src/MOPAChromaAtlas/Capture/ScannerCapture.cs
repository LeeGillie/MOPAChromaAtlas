using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MOPAChromaAtlas.Capture;

/// <summary>
/// Scans straight from a flatbed, where the platform has a standard way to do it:
/// Windows Image Acquisition on Windows (the scanner's own dialog opens, so resolution and
/// colour settings are set there), and SANE's <c>scanimage</c> on Linux. macOS has no such
/// command-line route; there, and for scanners without a WIA driver, use the scanner's own
/// software and the watched folder instead.
/// </summary>
public static class ScannerCapture
{
    /// <summary>WIA's identifier for PNG output (lossless: JPEG would change the colours).</summary>
    private const string WiaFormatPng = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";

    public static bool IsSupported =>
        OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && FindOnPath("scanimage") is not null);

    public static string Unsupported =>
        OperatingSystem.IsMacOS()
            ? "Scanning directly is not available on macOS. Scan with the scanner's own app into a folder, and watch that folder."
            : "No scanner interface found (on Linux, install SANE's scanimage). Scan with the scanner's own app into a folder, and watch that folder.";

    /// <summary>Scans one page to a new PNG in <paramref name="folder"/>. Returns its path, or null if cancelled.</summary>
    public static Task<string?> ScanAsync(string folder, int dpi = 600)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"scan-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        if (OperatingSystem.IsWindows()) return ScanWiaAsync(path);
        if (OperatingSystem.IsLinux()) return ScanSaneAsync(path, dpi);
        throw new PlatformNotSupportedException(Unsupported);
    }

    /// <summary>
    /// WIA through its COM automation layer (present on every Windows install). It must run
    /// on a single-threaded apartment, so it gets its own thread rather than borrowing the UI's.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Task<string?> ScanWiaAsync(string path)
    {
        var tcs = new TaskCompletionSource<string?>();
        var t = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("WIA.CommonDialog")
                           ?? throw new InvalidOperationException("Windows Image Acquisition is not available on this PC.");
                dynamic dialog = Activator.CreateInstance(type)!;
                // DeviceType 1 = scanner, Intent 1 = colour, Bias 131072 = maximise quality;
                // AlwaysSelectDevice false, UseCommonUI true (the scanner's dialog), CancelError false.
                dynamic? image = dialog.ShowAcquireImage(1, 1, 131072, WiaFormatPng, false, true, false);
                if (image is null) { tcs.SetResult(null); return; }
                if (File.Exists(path)) File.Delete(path);
                image.SaveFile(path);
                tcs.SetResult(path);
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x80210015)
            {
                tcs.SetException(new InvalidOperationException("No scanner was found. Check it is switched on and connected."));
            }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        return tcs.Task;
    }

    private static async Task<string?> ScanSaneAsync(string path, int dpi)
    {
        var psi = new ProcessStartInfo(FindOnPath("scanimage")!)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "--format=png", "--mode", "Color", "--resolution", dpi.ToString(), "--output-file", path })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start scanimage.");
        string err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new InvalidOperationException($"scanimage failed: {err.Trim()}");
        return File.Exists(path) ? path : null;
    }

    private static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);
}
