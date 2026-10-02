using Avalonia;

namespace MOPAChromaAtlas;

internal static class Program
{
    /// <summary>
    /// Command line, for documentation and testing rather than everyday use:
    ///   --atlas &lt;file&gt;       use this atlas file instead of the one in the user profile
    ///   --demo               open a throwaway atlas with a seed grid and SIMULATED results
    ///   --screenshot &lt;png&gt;  skip the splash, save the main window as a PNG, and exit
    ///   --scan-demo &lt;image&gt; [x,y x,y x,y]  open the card importer on an image (clicks optional)
    ///   --list-cameras &lt;txt&gt; write every camera and format the app can see to a text file, and exit
    ///   --grab-camera &lt;name&gt; &lt;folder&gt;  test-capture from matching cameras into a folder (report + PNGs), and exit
    ///   --lumos-probe &lt;txt&gt;  ask the laser's read-only light and camera-calibration endpoints, write the answers, and exit
    ///   --calib-demo &lt;png&gt; &lt;clicks&gt;, --send-demo &lt;png&gt;, --read-demo &lt;png&gt;  open a window on a test picture (screenshots)
    ///   --version            print "MOPAChromaAtlas x.y.z" and exit (the updater runs a new copy with this)
    ///   --whats-new          print this version's release notes and exit; exit code 1 if the built-in
    ///                        notes are not this version's (the release build checks this)
    ///   --update-feed &lt;json&gt; check for updates against a local release JSON instead of GitHub (testing)
    ///   --no-update-check    do not ask GitHub this run
    ///   --update             check for a newer release and install it now, without opening a window
    ///                        (with --update-feed, from a local release); prints what happened
    /// </summary>
    public static StartupOptions Options { get; private set; } = new();

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine($"{Updates.UpdateService.ProgramName} {BuildInfo.Version}");
            return;
        }
        if (args.Contains("--whats-new"))
        {
            var md = Updates.ReleaseNotes.Markdown;
            var notes = Updates.ReleaseNotes.For(md, BuildInfo.Version);
            Console.Write(notes is null ? $"No release notes for {BuildInfo.Version} are built in.{Environment.NewLine}"
                                        : Updates.ReleaseNotes.PlainText(notes));
            Environment.ExitCode = Updates.ReleaseNotes.DescribesThisBuild(md) ? 0 : 1;
            return;
        }
        Options = StartupOptions.Parse(args);
        Updates.UpdateService.FeedOverride = Options.UpdateFeed;
        Updates.UpdateService.CleanupLeftovers();
        if (args.Contains("--update")) { Environment.ExitCode = UpdateFromCommandLine(); return; }
        Updates.ReleaseNotes.Probe();
        if (Options.ListCamerasPath is { } list) { Capture.CameraProbe.Write(list); return; }
        if (Options.GrabCamera is { } grab) { Capture.CameraProbe.Grab(grab.Name, grab.Folder); return; }
        if (Options.LumosProbePath is { } lp) { Capture.CameraProbe.Lumos(Capture.CaptureSettings.Load().LumosAddress, lp); return; }
        HideHostConsole();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// A build made over the network share has no MOPAChromaAtlas.exe (Directory.Build.props), so
    /// Visual Studio starts it as "dotnet MOPAChromaAtlas.dll". dotnet.exe is a console program
    /// and Windows gives it a console window of its own. When that window belongs to this
    /// program alone, let it go before the main window opens. Started from a command prompt, the
    /// prompt shares the console, so it stays (the command-line options print there).
    /// </summary>
    private static void HideHostConsole()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (GetConsoleWindow() == IntPtr.Zero) return;
            var ids = new uint[4];
            if (GetConsoleProcessList(ids, (uint)ids.Length) == 1) FreeConsole();
        }
        catch (Exception) { /* a visible console is harmless */ }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    /// <summary>--update: the in-place update without a window. 0 updated or already current, 1 not updated.</summary>
    private static int UpdateFromCommandLine()
    {
        var info = Updates.UpdateService.CheckAsync(force: true).GetAwaiter().GetResult();
        if (info.Error is not null) { Console.WriteLine(info.Error); return 1; }
        if (!info.Newer) { Console.WriteLine($"MOPAChroma Atlas {BuildInfo.Version} is the latest version."); return 0; }
        if (!info.CanInstall || Updates.UpdateService.CurrentLayout() is not { } layout)
        {
            Console.WriteLine($"MOPAChroma Atlas {info.Latest} is available, but this copy cannot update itself: {info.InstallBlocker}");
            return 1;
        }
        try
        {
            var progress = new Progress<(string Stage, double? Fraction)>(p => { if (p.Fraction is null) Console.WriteLine(p.Stage + "..."); });
            Updates.UpdateService.InstallAsync(info, layout, progress).GetAwaiter().GetResult();
            Console.WriteLine($"Updated to MOPAChroma Atlas {info.Latest}.");
            return 0;
        }
        catch (Updates.UpdateException ex)
        {
            Console.WriteLine($"Not updated: {ex.Message} Nothing was changed.");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal sealed record StartupOptions
{
    public string? AtlasPath { get; init; }
    public bool Demo { get; init; }
    /// <summary>A local release JSON to check for updates against instead of GitHub (testing the updater).</summary>
    public string? UpdateFeed { get; init; }
    /// <summary>Do not check for updates this run.</summary>
    public bool NoUpdateCheck { get; init; }
    public string? ScreenshotPath { get; init; }
    public string? ScanDemoPath { get; init; }
    /// <summary>Opens the reader on an image of an 8 × 3 Atlas test card (8 mm cells, 2 mm gaps) - the one the AtlasCardTests engrave.</summary>
    public string? ReadDemoPath { get; init; }
    /// <summary>Opens the sheet reader on a picture of the sheet SheetReaderTests engraves (grids 5, 6, 7 and 12).</summary>
    public string? SheetDemoPath { get; init; }
    /// <summary>Opens the camera calibration on a picture and clicks the listed points (x,y per line), then saves it.</summary>
    public (string Image, string Clicks)? CalibDemo { get; init; }
    /// <summary>Opens Send to laser for the demo grid with this picture as the laser camera's.</summary>
    public string? SendDemoPath { get; init; }
    /// <summary>Opens the palette window on the demo atlas (simulated colours).</summary>
    public bool PaletteDemo { get; init; }
    /// <summary>Writes every camera FlashCap can see, with all its formats, to this text file and exits.</summary>
    public string? ListCamerasPath { get; init; }
    /// <summary>Test-captures from cameras whose name contains Name, writing a report and PNGs to Folder, and exits.</summary>
    public (string Name, string Folder)? GrabCamera { get; init; }
    /// <summary>Asks the laser's undocumented read-only status endpoints (light, camera calibration) and writes the answers here, then exits.</summary>
    public string? LumosProbePath { get; init; }
    public MOPAChroma.Scanning.PointD[] ScanDemoClicks { get; init; } = [];

    private static MOPAChroma.Scanning.PointD Pt(string s)
    {
        var p = s.Split(',');
        return new(double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                   double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    public static StartupOptions Parse(string[] args)
    {
        var o = new StartupOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            o = args[i] switch
            {
                "--atlas" => o with { AtlasPath = Path.GetFullPath(Next()) },
                "--demo" => o with { Demo = true },
                "--update-feed" => o with { UpdateFeed = Path.GetFullPath(Next()) },
                "--no-update-check" => o with { NoUpdateCheck = true },
                "--palette-demo" => o with { PaletteDemo = true },
                "--screenshot" => o with { ScreenshotPath = Path.GetFullPath(Next()) },
                // --scan-demo <image> [x1,y1 x2,y2 x3,y3] : open the importer on this image. With
                // corner clicks (image pixels) the grid is placed from them; without, it is found
                // automatically. For screenshots and checking fits.
                "--scan-demo" => o with
                {
                    ScanDemoPath = Path.GetFullPath(Next()),
                    ScanDemoClicks = i + 3 < args.Length && args[i + 1].Contains(',') ? [Pt(Next()), Pt(Next()), Pt(Next())] : [],
                },
                // --read-demo <image> : read back an 8 × 3 Atlas grid (as engraved by AtlasCardTests).
                "--lumos-probe" => o with { LumosProbePath = Path.GetFullPath(Next()) },
                "--grab-camera" => o with { GrabCamera = (Next(), Path.GetFullPath(Next())) },
                "--list-cameras" => o with { ListCamerasPath = Path.GetFullPath(Next()) },
                "--calib-demo" => o with { CalibDemo = (Path.GetFullPath(Next()), Path.GetFullPath(Next())) },
                "--send-demo" => o with { SendDemoPath = Path.GetFullPath(Next()) },
                "--read-demo" => o with { ReadDemoPath = Path.GetFullPath(Next()) },
                "--sheet-demo" => o with { SheetDemoPath = Path.GetFullPath(Next()) },
                _ => o,
            };
        }
        return o;
    }
}
