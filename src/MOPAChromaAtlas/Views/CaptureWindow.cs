using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MOPAChroma.AngleCapture;
using MOPAChroma.Atlas;
using MOPAChroma.Capture.Camera;
using MOPAChroma.Capture.Devices;
using MOPAChroma.Capture.Package;
using MOPAChroma.Capture.Rig;
using MOPAChroma.Capture.Run;
using MOPAChroma.Capture.Simulation;
using MOPAChroma.Grids;
using MOPAChroma.Scanning;

namespace MOPAChromaAtlas.Views;

/// <summary>
/// Runs the capture box (MOPAChroma Capture) on the selected grid's card and imports what
/// it measured: each cell's colour over tilt, turn and light. The box does everything
/// itself once started; Stop leaves it dark and flat. A simulated rig is offered in the
/// demo atlas only, so simulated colours can never reach a real one.
/// </summary>
public sealed class CaptureWindow : Window
{
    private readonly AtlasDocument _atlas;
    private readonly TestGrid _grid;
    private readonly Action _save;
    private readonly bool _demo;
    private readonly ComboBox _rigBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _cameraBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly RadioButton _quick = new() { Content = "Quick look: one turn, 7 tilts, 2 brightness steps", GroupName = "plan" };
    private readonly RadioButton _full = new() { Content = "Full: 2 turns, 41 tilts, 3 steps (~2,000 frames)", GroupName = "plan", IsChecked = true };
    private readonly TextBox _folder = new() { IsReadOnly = true };
    private readonly Button _start = new() { Content = "Start capture", Classes = { "accent" } };
    private readonly Button _stop = new() { Content = "Stop", IsEnabled = false };
    private readonly Button _import = new() { Content = "Import a saved capture…" };
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1, Height = 6 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly PhotoView _photo = new() { EmptyText = "The camera's pictures appear here during a run." };
    private CancellationTokenSource? _cts;
    private IReadOnlyList<FlashCap.CaptureDeviceDescriptor> _cameras = [];

    private const string SimulatedItem = "Simulated rig and camera (demo atlas only)";

    public CaptureWindow(AtlasDocument atlas, TestGrid grid, Action save, bool demo)
    {
        _atlas = atlas; _grid = grid; _save = save; _demo = demo;
        Title = $"Capture angles - {grid.Title}";
        Width = 1120; Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MOPAChroma Captures");

        ToolTip.SetTip(_rigBox, "The capture box's USB serial port. It answers HELLO like a rig; \"Find\" asks every port.");
        ToolTip.SetTip(_cameraBox, "The camera in the box, looking straight down. Its exposure, white balance, gain and focus are locked for the run (Windows and Linux).");
        ToolTip.SetTip(_quick, "A first look at a card, a few minutes.");
        ToolTip.SetTip(_full, "Everything the box can measure: fine tilt steps where the LEDs' reflections fall, along and across the fill lines.");
        ToolTip.SetTip(_folder, "Where capture packages are written: one folder per run, kept so a capture can be imported again.");
        ToolTip.SetTip(_start, "Check the lid, home both axes, lock the camera and run the plan. The laser is not involved.");
        ToolTip.SetTip(_stop, "Stop after the current frame. The LEDs go off and the card goes flat; what was captured is kept.");
        ToolTip.SetTip(_import, "Import a capture package made earlier (for example with the command-line tool).");

        var find = new Button { Content = "Find" };
        ToolTip.SetTip(find, "Ask every serial port for the capture box.");
        find.Click += async (_, _) => await FindRigAsync();
        var choose = new Button { Content = "…" };
        ToolTip.SetTip(choose, "Choose where capture packages are written.");
        choose.Click += async (_, _) =>
        {
            var f = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Where to write capture packages" });
            if (f.Count > 0 && f[0].TryGetLocalPath() is { } p) _folder.Text = p;
        };
        _start.Click += async (_, _) => await RunAsync();
        _stop.Click += (_, _) => { _cts?.Cancel(); _status.Text = "Stopping after this frame…"; };
        _import.Click += async (_, _) => await ImportSavedAsync();
        Closing += (_, e) => { if (_cts is not null) { e.Cancel = true; _status.Text = "Stop the capture first."; } };

        var rigRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        rigRow.Children.Add(_rigBox); Grid.SetColumn(find, 1); rigRow.Children.Add(find);
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        folderRow.Children.Add(_folder); Grid.SetColumn(choose, 1); folderRow.Children.Add(choose);

        var side = new StackPanel
        {
            Width = 380, Spacing = 10, Margin = new Thickness(14, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = grid.Title, FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"{grid.Cells.Count} cells. Put the card in the turntable's pocket, labels upright, and close the lid.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8 },
                new TextBlock { Text = "Capture box", FontWeight = FontWeight.SemiBold }, rigRow,
                new TextBlock { Text = "Camera", FontWeight = FontWeight.SemiBold }, _cameraBox,
                new TextBlock { Text = "Plan", FontWeight = FontWeight.SemiBold }, _quick, _full,
                new TextBlock { Text = "Packages folder", FontWeight = FontWeight.SemiBold }, folderRow,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _start, _stop, _import } },
                _bar, _status, new ScrollViewer { Content = _result, MaxHeight = 220 },
            },
        };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14) };
        body.Children.Add(_photo);
        Grid.SetColumn(side, 1); body.Children.Add(side);
        Content = body;

        Opened += async (_, _) => await LoadDevicesAsync();
    }

    /// <summary>For the --capture-demo screenshot: a quick simulated run into this folder, then its import.</summary>
    internal async Task RunDemoAsync(string folder)
    {
        _folder.Text = folder;
        _quick.IsChecked = true;
        if (_rigBox.Items.Count == 0) await LoadDevicesAsync();
        _rigBox.SelectedItem = SimulatedItem;
        await RunAsync();
    }

    private async Task LoadDevicesAsync()
    {
        _rigBox.Items.Clear();
        if (_demo) _rigBox.Items.Add(SimulatedItem);
        foreach (var p in RigPorts.List()) _rigBox.Items.Add(p);
        _rigBox.SelectedIndex = _rigBox.Items.Count > 0 ? 0 : -1;
        _status.Text = "Looking for cameras…";
        _cameras = await Task.Run(FlashCapCamera.List);
        _cameraBox.Items.Clear();
        foreach (var c in _cameras) _cameraBox.Items.Add(c.Name);
        _cameraBox.SelectedIndex = _cameras.Count > 0 ? 0 : -1;
        _status.Text = _demo
            ? "Demo atlas: the simulated rig is available. Its results are labelled SIMULATED."
            : _rigBox.Items.Count == 0 ? "No serial ports found. Plug in the capture box and press Find." : "Ready.";
    }

    private async Task FindRigAsync()
    {
        _status.Text = "Asking every serial port…";
        var found = await RigPorts.FindAsync();
        if (found is { } f)
        {
            if (!_rigBox.Items.Contains(f.Port)) _rigBox.Items.Add(f.Port);
            _rigBox.SelectedItem = f.Port;
            _status.Text = $"Capture box on {f.Port}: {f.Info.Raw}";
        }
        else _status.Text = "No capture box answered. Is it plugged in and powered (12 V)?";
    }

    private async Task RunAsync()
    {
        bool sim = _rigBox.SelectedItem as string == SimulatedItem;
        if (sim && !_demo) { _status.Text = "The simulated rig is only for the demo atlas."; return; }
        if (!sim && _rigBox.SelectedItem is not string port) { _status.Text = "Choose the capture box's port (or press Find)."; return; }
        if (!sim && _cameraBox.SelectedIndex < 0) { _status.Text = "No camera chosen."; return; }
        Directory.CreateDirectory(_folder.Text!);

        var geometry = new MOPAChroma.Capture.Geometry.RigGeometry();
        IRigTransport transport;
        ICaptureCamera camera;
        var plan = _quick.IsChecked == true ? CapturePlan.Quick : new CapturePlan();
        try
        {
            if (sim)
            {
                var rigSim = new SimulatedRig();
                transport = rigSim;
                var src = _atlas.Sources.First(s => s.Id == _grid.SourceId);
                var lens = _atlas.Lenses.First(l => l.Id == _grid.LensId);
                camera = new SimulatedCamera(rigSim, new SimScene { Rig = geometry, Width = 960, Height = 720, PixelsPerMm = 3.4, Cells = CaptureCards.SimulatedCells(_grid, src, lens) });
                plan = plan with { LedSettle = TimeSpan.Zero, TiltSettle = TimeSpan.Zero, DiscardAfterChange = 0, GrabsPerShot = 1 };
            }
            else
            {
                transport = new SerialRigTransport((string)_rigBox.SelectedItem!);
                camera = new FlashCapCamera(_cameras[_cameraBox.SelectedIndex]);
            }
        }
        catch (Exception e) { _status.Text = "Could not open the box or camera: " + e.Message; return; }

        _cts = new CancellationTokenSource();
        _start.IsEnabled = false; _import.IsEnabled = false; _stop.IsEnabled = true;
        _result.Text = "";
        CaptureResult? result = null;
        await using (var rig = new RigClient(transport))
        await using (camera)
        {
            var runner = new CaptureRunner(rig, camera, geometry) { Writer = $"MOPAChroma Atlas {BuildInfo.Version}" };
            var progress = new Progress<CaptureProgress>(p =>
            {
                _bar.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
                _status.Text = $"{p.Done} of {p.Total}: {p.Step}";
                if (p.LastFrame is { } f && (p.Done % 3 == 0 || p.LastRecord?.Kind != FrameKind.Lit))
                    _photo.SetPicture(new RgbImage(f.Width, f.Height, f.Pixels));
            });
            try
            {
                result = await runner.RunAsync(plan, new GridRef { AtlasGridId = _grid.Id, Label = _grid.Title, HatchAngleDeg = _grid.Cells.FirstOrDefault()?.Settings.HatchAngleDeg },
                                               _folder.Text!, sim, progress, _cts.Token);
            }
            catch (Exception e) { _status.Text = e.Message; }
        }
        _cts = null;
        _start.IsEnabled = true; _import.IsEnabled = true; _stop.IsEnabled = false;
        if (result is null) return;

        var lines = new List<string> { result.Complete ? "Capture finished." : $"Capture not finished: {result.Error}", $"Quality: {result.Checks.Overall}" };
        lines.AddRange(result.Checks.Run.Select(c => $"  {c.Status}: {c.Detail}"));
        _result.Text = string.Join("\n", lines);
        await ImportAsync(CapturePackage.Load(result.Folder), sim);
    }

    private async Task ImportSavedAsync()
    {
        var f = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "A capture package folder" });
        if (f.Count == 0 || f[0].TryGetLocalPath() is not { } path) return;
        CapturePackage pkg;
        try { pkg = CapturePackage.Load(path); }
        catch (Exception e) { _status.Text = e.Message; return; }
        if (pkg.Session.Grid.AtlasGridId != _grid.Id)
        {
            _status.Text = "That package is of a different grid. Select its grid in the main window first.";
            return;
        }
        await ImportAsync(pkg, pkg.Session.Simulated);
    }

    private async Task ImportAsync(CapturePackage pkg, bool simulated)
    {
        if (simulated && !_demo) { _status.Text = "Simulated packages are only imported into the demo atlas."; return; }
        _status.Text = "Finding the grid on the plain frame…";
        CellMap? map;
        string note;
        if (simulated) (map, note) = (CellMap.Centred(_grid), "Simulated card: cells where the simulation drew them.");
        else (map, note) = await Task.Run(() => CaptureCards.FindOnPlainFrame(pkg, _grid));
        if (map is null)
        {
            _status.Text = note + " The package is kept; placing the grid by hand is not built yet (see TODO).";
            return;
        }
        _status.Text = "Measuring every cell in every frame…";
        var progress = new Progress<(int Done, int Total)>(p => { _bar.Value = (double)p.Done / Math.Max(1, p.Total); });
        try
        {
            var report = await Task.Run(() => CaptureImport.Import(_atlas, pkg, map, allowSimulated: _demo, progress));
            _save();
            _status.Text = $"Imported {report.Sets.Count} cells, {report.Points} readings. {note}";
            if (report.Notes.Count > 0) _result.Text += "\n" + string.Join("\n", report.Notes);
        }
        catch (Exception e) { _status.Text = "Import failed: " + e.Message; }
    }
}
