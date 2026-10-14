using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MOPAChroma.Grids;
using MOPAChroma.Scanning;
using MOPAChromaAtlas.Views;

namespace MOPAChromaAtlas;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var opts = Program.Options;
            if (opts.PaletteDemo)
            {
                var doc = DemoAtlas.Create();
                var g = doc.Grids[0];
                var w = new PaletteWindow(doc.Materials.First(m => m.Id == g.MaterialId),
                                          doc.Sources.First(x => x.Id == g.SourceId), doc.Lenses.First(x => x.Id == g.LensId), doc.Samples);
                desktop.MainWindow = w;
                if (opts.ScreenshotPath is { } shot7)
                    w.Opened += (_, _) => DispatcherTimer.RunOnce(() => { SaveWindow(w, shot7); desktop.Shutdown(); }, TimeSpan.FromMilliseconds(800));
            }
            else if (opts.CaptureDemoFolder is { } capDir)
            {
                var doc = DemoAtlas.Create();
                var w = new CaptureWindow(doc, doc.Grids[0], () => { }, demo: true);
                desktop.MainWindow = w;
                w.Opened += async (_, _) =>
                {
                    await w.RunDemoAsync(capDir);
                    if (opts.ScreenshotPath is { } shot8)
                        DispatcherTimer.RunOnce(() => { SaveWindow(w, shot8); desktop.Shutdown(); }, TimeSpan.FromMilliseconds(800));
                };
            }
            else if (opts.CalibDemo is { } calib)
            {
                var w = new CameraCalibrationWindow();
                desktop.MainWindow = w;
                w.Opened += (_, _) =>
                {
                    if (File.Exists(calib.Image)) w.UsePicture(LumosUltra.Camera.RgbImage.Load(calib.Image));
                    if (File.Exists(calib.Clicks))
                    foreach (var line in File.ReadAllLines(calib.Clicks).Where(l => l.Contains(',')))
                    {
                        var p = line.Split(',');
                        w.ClickAt(new(double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    DispatcherTimer.RunOnce(() =>
                    {
                        if (opts.ScreenshotPath is { } shot5) SaveWindow(w, shot5);
                        w.SaveCalibration();
                        desktop.Shutdown();
                    }, TimeSpan.FromMilliseconds(800));
                };
            }
            else if (opts.SendDemoPath is { } sendImg)
            {
                SendToLaserWindow.AutoPhoto = false;
                var w = new SendToLaserWindow(DemoGrid() with { Number = 29 }, DemoContext());
                desktop.MainWindow = w;
                w.Opened += (_, _) =>
                {
                    w.UsePicture(LumosUltra.Camera.RgbImage.Load(sendImg));
                    // MOPACHROMA_DEMO_CENTRE=x,y puts the job there (to compare with a simulated engraving).
                    if (Environment.GetEnvironmentVariable("MOPACHROMA_DEMO_CENTRE") is { } dc && dc.Split(',') is [var dx, var dy])
                        w.SetJobCentre(double.Parse(dx, System.Globalization.CultureInfo.InvariantCulture), double.Parse(dy, System.Globalization.CultureInfo.InvariantCulture));
                    if (opts.ScreenshotPath is { } shot6)
                        DispatcherTimer.RunOnce(() => { SaveWindow(w, shot6); desktop.Shutdown(); }, TimeSpan.FromMilliseconds(800));
                };
            }
            else if (opts.SheetDemoPath is { } sheetImg)
            {
                var atlas = new MOPAChroma.Atlas.AtlasDocument();
                foreach (var (cols, rows, cell, gap, n) in new[] { (8, 3, 8.0, 2.0, 5), (8, 3, 8.0, 2.0, 6), (8, 3, 8.0, 2.0, 7), (9, 5, 4.0, 1.0, 12) })
                {
                    var layout = new GridLayout { Columns = cols, Rows = rows, CellSizeMm = cell, GapMm = gap };
                    atlas.Grids.Add(new TestGrid
                    {
                        Title = $"Stainless — grid {n}", MaterialId = "ss-mirror", Number = n,
                        SourceId = LumosUltra.Jobs.SourceProfile.Presets[0].Id, LensId = LumosUltra.Jobs.LensProfile.Presets[0].Id,
                        Layout = layout,
                        Cells = Enumerable.Range(0, rows * cols).Select(i => new GridCell(i / cols, i % cols, GridLayout.Label(i / cols, i % cols),
                            new LumosUltra.Jobs.MopaSettings { PowerPercent = 40 + i, SpeedMmPerS = 500, LineDensityPerCm = 300, FrequencyKHz = 200, PulseWidthNs = 60, Passes = 1 })).ToList(),
                    });
                }
                desktop.MainWindow = new SheetReadWindow(atlas, LumosUltra.Camera.RgbImage.Load(sheetImg), System.IO.Path.GetFileName(sheetImg));
            }
            else if (opts.ReadDemoPath is { } readImg)
            {
                var grid = DemoGrid();
                var w = new ScanImportWindow(DemoContext() with { Existing = grid });
                desktop.MainWindow = w;
                w.Opened += async (_, _) =>
                {
                    var img = LumosUltra.Camera.RgbImage.Load(readImg);
                    // MOPACHROMA_DEMO_CENTRE=x,y: the picture shows this grid engraved there; place it from the camera calibration.
                    MOPAChroma.Scanning.GridGeometry? at = null;
                    if (Environment.GetEnvironmentVariable("MOPACHROMA_DEMO_CENTRE") is { } dc && dc.Split(',') is [var dx, var dy]
                        && Capture.CaptureSettings.Load().CurrentLumosCamera is { } cam)
                        at = cam.GridAt(grid.Layout, double.Parse(dx, System.Globalization.CultureInfo.InvariantCulture), double.Parse(dy, System.Globalization.CultureInfo.InvariantCulture));
                    await w.UseCaptureAsync(new MOPAChroma.Scanning.CardCapture(img, MOPAChroma.Scanning.CaptureKind.Photo, "Lumos Ultra camera", Path.GetFileName(readImg), readImg), at);
                    if (opts.ScreenshotPath is { } shot4)
                        DispatcherTimer.RunOnce(() => { SaveWindow(w, shot4); desktop.Shutdown(); }, TimeSpan.FromMilliseconds(800));
                };
            }
            else if (opts.ScanDemoPath is { } scan)
            {
                var w = new ScanImportWindow(new ScanImportContext(
                    new MOPAChroma.Atlas.Material { Id = "ss-mirror", Name = "Stainless steel", Finish = "mirror" },
                    LumosUltra.Jobs.SourceProfile.Presets[0], LumosUltra.Jobs.LensProfile.Presets[0],
                    [], null, "ss-mirror-G01"));
                desktop.MainWindow = w;
                w.Opened += async (_, _) =>
                {
                    await w.LoadAsync(scan, opts.ScanDemoClicks.Length == 3 ? opts.ScanDemoClicks : null);
                    if (opts.ScreenshotPath is { } shot2)
                        DispatcherTimer.RunOnce(() => { SaveWindow(w, shot2); desktop.Shutdown(); },
                                                TimeSpan.FromMilliseconds(800));
                };
            }
            else if (opts.ScreenshotPath is { } shot)
            {
                var main = new MainWindow();
                desktop.MainWindow = main;
                main.Opened += (_, _) => DispatcherTimer.RunOnce(() =>
                {
                    SaveWindow(main, shot);
                    desktop.Shutdown();
                }, TimeSpan.FromMilliseconds(800));
            }
            else
            {
                // Splash first, then the main window.
                var splash = new SplashWindow();
                desktop.MainWindow = splash;
                DispatcherTimer.RunOnce(() =>
                {
                    var main = new MainWindow();
                    desktop.MainWindow = main;
                    main.Show();
                    splash.Close();
                }, TimeSpan.FromMilliseconds(1600));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The 8 × 3 Atlas grid the demos and AtlasCardTests use (8 mm cells, 2 mm gaps; first two columns too weak to mark).</summary>
    private static MOPAChroma.Grids.TestGrid DemoGrid()
    {
        var layout = new MOPAChroma.Grids.GridLayout { Columns = 8, Rows = 3, CellSizeMm = 8, GapMm = 2 };
        var cells = new List<MOPAChroma.Grids.GridCell>();
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 8; c++)
                cells.Add(new(r, c, MOPAChroma.Grids.GridLayout.Label(r, c), new LumosUltra.Jobs.MopaSettings
                {
                    PowerPercent = c < 2 ? 20 + c * 5 : 40 + c * 7, SpeedMmPerS = 800 - r * 100, LineDensityPerCm = 300,
                    FrequencyKHz = 150 + r * 50, PulseWidthNs = 60, Passes = 1,
                }));
        return new MOPAChroma.Grids.TestGrid
        {
            Title = "Seed (demo)", MaterialId = "ss-mirror", SourceId = LumosUltra.Jobs.SourceProfile.Presets[0].Id,
            LensId = LumosUltra.Jobs.LensProfile.Presets[0].Id, Layout = layout, Cells = cells,
        };
    }

    private static ScanImportContext DemoContext() => new(
        new MOPAChroma.Atlas.Material { Id = "ss-mirror", Name = "Stainless steel", Finish = "mirror" },
        LumosUltra.Jobs.SourceProfile.Presets[0], LumosUltra.Jobs.LensProfile.Presets[0], [], null, "demo");

    private static void SaveWindow(Avalonia.Controls.Window w, string path)
    {
        var size = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
        using var bmp = new RenderTargetBitmap(size);
        bmp.Render(w);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bmp.Save(path);
    }
}
