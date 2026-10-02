using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Scanning;
using MOPAChromaAtlas.Capture;

namespace MOPAChromaAtlas.Views;

/// <summary>
/// What the dialog hands back when the user adds a card to the atlas. With
/// <c>ForExistingGrid</c> the grid is already in the atlas (an Atlas grid that was
/// engraved and read back) and only its results are new.
/// </summary>
public sealed record ScanImportResult(TestGrid Grid, IReadOnlyList<Sample> Samples, Guid? PlannedCardId, bool ForExistingGrid = false);

/// <summary>
/// Everything the importer needs to know about the atlas to fill itself in.
/// <c>Previous</c> is the most recent card of this material, used for sensible defaults.
/// </summary>
public sealed record ScanImportContext(
    Material Material, SourceProfile Source, LensProfile Lens,
    IReadOnlyList<PlannedCard> Planned, TestGrid? Previous, string NextCardId)
{
    /// <summary>
    /// Set to read back an Atlas grid that is already in the atlas: its layout and cells
    /// are known, so there is nothing to describe, and its corner crosses (rather than
    /// MakeIt's labels) show which way up the card is.
    /// </summary>
    public TestGrid? Existing { get; init; }
}

/// <summary>
/// Reads a test card (MakeIt Ultra colour test, Material Testing Array, or an Atlas grid)
/// from an image: pick or describe the card, bring in a scan or photo (file, clipboard,
/// drag and drop, scanner, scan folder, USB camera or the laser's camera), let the squares
/// be found automatically - or click three corner squares - check the suggested results,
/// and add the card to the atlas.
/// </summary>
public partial class ScanImportWindow : Window
{
    private static readonly Outcome[] Cycle = [Outcome.Good, Outcome.NoMark, Outcome.Uneven, Outcome.Damaged];
    private static readonly CardAxis[] Axes = Enum.GetValues<CardAxis>();

    private readonly ScanImportContext _ctx;
    private Material _material => _ctx.Material;
    private SourceProfile _source => _ctx.Source;
    private LensProfile _lens => _ctx.Lens;

    private static readonly CaptureKind[] Kinds = [CaptureKind.FlatbedScan, CaptureKind.Photo];

    private CardCapture? _capture;
    private RgbImage? _img;
    private readonly List<PointD> _clicks = [];
    private bool _manual;
    private GridFitResult? _fit;
    private GridDetection? _detection;
    private int _turn;
    private ScanMeasurement? _measure;
    private Outcome[,]? _outcomes;
    private bool _filling;
    private readonly CaptureSettings _settings = CaptureSettings.Load();
    private ScanFolderWatcher? _watcher;
    private CancellationTokenSource? _findCts;

    // View: zoom (1 = whole picture) about an image point at the centre of the view.
    private double _zoom = 1;
    private PointD? _centre;
    // Dragging on the picture.
    private enum DragKind { None, Pan, Grid, Corner, Crop }
    private DragKind _drag;
    private Point _dragScreen;
    private PointD _dragImage;
    private PointD _panCentre;
    private int _dragCorner;
    private GridGeometry? _dragStartGrid, _dragGrid;
    private bool _cropMode;
    private Point? _cropEnd;
    // Before cropping: the whole picture, and where the crop sits in it.
    private CardCapture? _uncropped;
    private PointD _cropOffset;
    // The grid as the user placed it is kept exactly: measured where the boxes are, never
    // moved by the software unless asked (Snap to squares, Find automatically).
    private bool _handPlaced;
    // The grid came from the job's own engraving position (see UseCaptureAsync).
    private bool _placedFromJob;
    // How the picture is shown (never what is measured): exposure in stops, and contrast stretch.
    private int _viewStops;
    private bool _viewStretch;
    private readonly Stack<(GridFitResult Fit, GridDetection? Detection, int Turn, bool Manual, bool Hand)> _history = new();

    public ScanImportWindow() : this(new ScanImportContext(
        new Material { Id = "preview", Name = "Preview" }, SourceProfile.Presets[0], LensProfile.Presets[0], [], null, "preview-G01")) { }

    public ScanImportWindow(ScanImportContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        Title = ctx.Existing is { } eg ? $"Read {eg.Title} — {ctx.Material.DisplayName}" : $"Import a test card — {ctx.Material.DisplayName}";
        if (ctx.Existing is { } ex)
        {
            DefinitionPanel.IsVisible = false;
            ExistingPanel.IsVisible = true;
            ExistingTitle.Text = ex.Title;
            ExistingText.Text = $"{ex.Cells.Count} cells, {ex.Layout.Columns} × {ex.Layout.Rows}, {ex.Layout.CellSizeMm:0.#} mm squares {ex.Layout.GapMm:0.#} mm apart. " +
                                "Its corner crosses show which way up the card is; results go onto this grid's cells.";
        }

        var axisNames = Axes.Select(ColorTestDefinition.Name).ToList();
        RowAxisBox.ItemsSource = axisNames;
        ColAxisBox.ItemsSource = axisNames;

        PlannedBox.ItemsSource = new[] { "New card (set up in MakeIt by hand)" }
            .Concat(ctx.Planned.Select(p => $"Planned: {p.Display}")).ToList();
        PlannedBox.SelectedIndex = ctx.Planned.Count > 0 ? 1 : 0;

        // Card record defaults: carry lot, cleaning and version over from the last card.
        LotBox.Text = ctx.Previous?.Card?.Lot ?? "";
        CleaningBox.Text = ctx.Previous?.Card?.Cleaning ?? "IPA wipe";
        VersionBox.Text = ctx.Previous?.Card?.MakeItVersion ?? "";
        ApplyPlannedSelection();

        PlannedBox.SelectionChanged += (_, _) => ApplyPlannedSelection();
        UltraPresetButton.Click += (_, _) => Fill(ColorTestDefinition.UltraColourTest() with { FocusZMm = (double)(ZBox.Value ?? 0) });
        ArrayPresetButton.Click += (_, _) => Fill(ColorTestDefinition.MaterialTestArray() with { FocusZMm = (double)(ZBox.Value ?? 0) });
        KindBox.ItemsSource = new[] { "Flatbed scan (colours comparable)", "Photo (colours estimated)" };
        KindBox.SelectedIndex = 0;
        KindBox.SelectionChanged += (_, _) =>
        {
            if (_capture is not null && KindBox.SelectedIndex >= 0) _capture = _capture with { Kind = Kinds[KindBox.SelectedIndex] };
        };

        OpenButton.Click += async (_, _) => await OpenAsync();
        PasteButton.Click += async (_, _) => await PasteAsync();
        ScanButton.Click += async (_, _) => await ScanAsync();
        CameraButton.Click += async (_, _) => await CameraAsync();
        LumosButton.Click += async (_, _) => await LumosAsync();
        WatchFolderButton.Click += async (_, _) => await ChooseWatchFolderAsync();
        WatchBox.IsChecked = _settings.WatchEnabled && Directory.Exists(_settings.WatchFolder ?? "");
        WatchBox.IsCheckedChanged += (_, _) => UpdateWatcher();
        ViewHost.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ViewHost.AddHandler(DragDrop.DropEvent, OnDrop);
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control) && FocusManager?.GetFocusedElement() is not TextBox)
            {
                e.Handled = true;
                await PasteAsync();
            }
        };
        Closed += (_, _) => { _watcher?.Dispose(); _findCts?.Cancel(); };
        UpdateWatcher();

        FindButton.Click += async (_, _) => await FindAsync();
        ResetButton.Click += (_, _) => PlaceByHand();
        SnapButton.Click += (_, _) => Snap();
        UndoButton.Click += (_, _) => Undo();
        TurnButton.Click += (_, _) => TurnHalf();
        CropButton.Click += (_, _) => SetCropMode(!_cropMode);
        UndoCropButton.Click += async (_, _) => await UndoCropAsync();
        DarkerButton.Click += (_, _) => SetViewLevel(_viewStops - 1, _viewStretch);
        BrighterButton.Click += (_, _) => SetViewLevel(_viewStops + 1, _viewStretch);
        StretchButton.IsCheckedChanged += (_, _) => SetViewLevel(_viewStops, StretchButton.IsChecked == true);
        TurnPictureButton.Click += async (_, _) => await ReorientAsync(turn: true);
        MirrorPictureButton.Click += async (_, _) => await ReorientAsync(turn: false);
        ZoomInButton.Click += (_, _) => ZoomBy(1.5, null);
        ZoomOutButton.Click += (_, _) => ZoomBy(1 / 1.5, null);
        ZoomGridButton.Click += (_, _) => ZoomToGrid();
        FitViewButton.Click += (_, _) => { _zoom = 1; _centre = null; Redraw(); };
        CancelButton.Click += (_, _) => Close(null);
        SaveButton.Click += (_, _) => Save();
        ViewHost.PointerPressed += OnViewPressed;
        ViewHost.PointerMoved += OnViewMoved;
        ViewHost.PointerReleased += OnViewReleased;
        ViewHost.PointerCaptureLost += (_, _) => { if (_drag != DragKind.None) { _drag = DragKind.None; _dragGrid = null; Redraw(); } };
        ViewHost.PointerWheelChanged += OnViewWheel;
        ViewHost.SizeChanged += (_, _) => Redraw();
        AddHandler(KeyDownEvent, OnArrowKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        foreach (var box in new[] { RowMinBox, RowMaxBox, RowCountBox, ColMinBox, ColMaxBox, ColCountBox,
                                    PowerBox, SpeedBox, FreqBox, PwBox, DensityBox, PassesBox, ZBox })
            box.ValueChanged += (_, _) => DefinitionChanged();
        RowAxisBox.SelectionChanged += (_, _) => DefinitionChanged();
        ColAxisBox.SelectionChanged += (_, _) => DefinitionChanged();
        RowDescBox.IsCheckedChanged += (_, _) => DefinitionChanged();
        ColDescBox.IsCheckedChanged += (_, _) => DefinitionChanged();

        UpdateSteps();
    }

    private PlannedCard? SelectedPlanned =>
        PlannedBox.SelectedIndex >= 1 ? _ctx.Planned[PlannedBox.SelectedIndex - 1] : null;

    private void ApplyPlannedSelection()
    {
        if (SelectedPlanned is { } p)
        {
            Fill(p.Definition);
            CardIdBox.Text = p.CardId;
        }
        else
        {
            Fill(_ctx.Previous?.Definition ?? ColorTestDefinition.UltraColourTest());
            CardIdBox.Text = _ctx.NextCardId;
        }
    }

    private void Fill(ColorTestDefinition d)
    {
        _filling = true;
        RowAxisBox.SelectedIndex = Array.IndexOf(Axes, d.RowSweep.Axis);
        RowMinBox.Value = (decimal)d.RowSweep.Min;
        RowMaxBox.Value = (decimal)d.RowSweep.Max;
        RowCountBox.Value = d.RowSweep.Count;
        RowDescBox.IsChecked = d.RowSweep.Descending;
        ColAxisBox.SelectedIndex = Array.IndexOf(Axes, d.ColumnSweep.Axis);
        ColMinBox.Value = (decimal)d.ColumnSweep.Min;
        ColMaxBox.Value = (decimal)d.ColumnSweep.Max;
        ColCountBox.Value = d.ColumnSweep.Count;
        ColDescBox.IsChecked = d.ColumnSweep.Descending;
        PowerBox.Value = (decimal)d.PowerPercent;
        SpeedBox.Value = (decimal)d.SpeedMmPerS;
        FreqBox.Value = (decimal)d.FrequencyKHz;
        PwBox.Value = (decimal)d.PulseWidthNs;
        DensityBox.Value = (decimal)d.LineDensity;
        PassesBox.Value = d.Passes;
        ZBox.Value = (decimal)d.FocusZMm;
        _filling = false;
        DefinitionChanged();
    }

    private void DefinitionChanged()
    {
        if (_filling) return;
        var d = Definition();
        PowerBox.IsEnabled = !d.IsSwept(CardAxis.Power);
        SpeedBox.IsEnabled = !d.IsSwept(CardAxis.Speed);
        FreqBox.IsEnabled = !d.IsSwept(CardAxis.Frequency);
        ProblemText.Text = d.Problem() ?? "";
        if (_manual) { if (_clicks.Count == 3) Fit(); }
        else if (_img is not null) _ = FindAsync(debounce: true);
        UpdateSteps();
    }

    private static double V(NumericUpDown b, double fallback) => b.Value is { } v ? (double)v : fallback;

    private ColorTestDefinition Definition() => new()
    {
        RowSweep = new AxisSweep(Axes[Math.Max(0, RowAxisBox.SelectedIndex)], V(RowMinBox, 0), V(RowMaxBox, 1),
                                 (int)V(RowCountBox, 2), RowDescBox.IsChecked == true),
        ColumnSweep = new AxisSweep(Axes[Math.Max(0, ColAxisBox.SelectedIndex)], V(ColMinBox, 0), V(ColMaxBox, 1),
                                    (int)V(ColCountBox, 2), ColDescBox.IsChecked == true),
        PowerPercent = V(PowerBox, 50),
        SpeedMmPerS = V(SpeedBox, 800),
        FrequencyKHz = V(FreqBox, 200),
        PulseWidthNs = V(PwBox, 6),
        LineDensity = V(DensityBox, 5000),
        Passes = (int)V(PassesBox, 1),
        FocusZMm = V(ZBox, 0),
    };

    // The card being read: an existing Atlas grid, or a MakeIt card described on the left.
    private int GridRows => _ctx.Existing?.Layout.Rows ?? Definition().Rows;
    private int GridColumns => _ctx.Existing?.Layout.Columns ?? Definition().Columns;
    private string? CardProblem => _ctx.Existing is null ? Definition().Problem() : null;
    private CardMarks Marks => _ctx.Existing is { } g ? CardMarks.AtlasCorners(g.Layout) : CardMarks.MakeItLabels;

    private string DescribeCellAt(int row, int col)
    {
        if (_ctx.Existing is not { } g) return Definition().DescribeCell(row, col);
        var c = g.Cells.FirstOrDefault(x => x.Row == row && x.Column == col);
        return c is null ? $"row {row + 1}, column {col + 1} (no planned cell)" : $"{c.Label}: {c.Settings.ToShortString()}";
    }

    // ---------------------------------------------------------------- bringing an image in

    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a scan or photo of the test card",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Scans and photos") { Patterns = ["*.png", "*.tif", "*.tiff", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await LoadAsync(path);
    }

    /// <summary>
    /// Load an image file and, optionally, apply corner clicks in image pixels. Used by the
    /// Open button, drag and drop, the scan folder, and --scan-demo (documentation
    /// screenshots). Without clicks the squares are found automatically.
    /// </summary>
    public async Task LoadAsync(string path, IReadOnlyList<PointD>? clicks = null, CaptureKind? kind = null, string source = "File")
    {
        StatusText.Text = $"Reading {Path.GetFileName(path)}…";
        RgbImage img;
        try { img = await Task.Run(() => RgbImage.Load(path)); }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not read that file: {ex.Message}";
            return;
        }
        var capture = new CardCapture(img, kind ?? CardCapture.GuessKind(img), source, Path.GetFileName(path), path);
        await UseAsync(capture, clicks);
    }

    /// <summary>
    /// Keeps a capture that did not come from a file (camera, laser, clipboard) as a PNG in
    /// the captures folder, so every reading in the atlas can be traced back to an image.
    /// </summary>
    private static CardCapture Keep(CardCapture c, string tag)
    {
        try
        {
            Directory.CreateDirectory(CaptureSettings.CapturesFolder);
            var path = Path.Combine(CaptureSettings.CapturesFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{tag}.png");
            c.Image.SavePng(path);
            return c with { Name = Path.GetFileName(path), FilePath = path };
        }
        catch { return c; }
    }

    /// <summary>Opens with an image already taken (e.g. the laser's camera right after a job).</summary>
    /// <param name="capture">The picture.</param>
    /// <param name="engravedAt">
    /// Where the grid's squares are in this picture, when that is known: a job just engraved
    /// at a chosen bed position and photographed by the calibrated laser camera. The grid is
    /// then put there directly instead of being searched for.
    /// </param>
    public Task UseCaptureAsync(CardCapture capture, GridGeometry? engravedAt = null) => UseAsync(capture, engravedAt: engravedAt);

    private async Task UseAsync(CardCapture capture, IReadOnlyList<PointD>? clicks = null, bool cropped = false, GridGeometry? engravedAt = null)
    {
        if (!cropped) { _uncropped = null; _cropOffset = new PointD(0, 0); }
        UndoCropButton.IsVisible = _uncropped is not null;
        SetCropMode(false);
        _capture = capture;
        _img = capture.Image;
        _history.Clear();
        UndoButton.IsEnabled = false;
        RefreshPicture();
        _zoom = 1;
        _centre = null;
        EmptyText.IsVisible = false;
        ViewTools.IsVisible = true;
        ViewHintBorder.IsVisible = true;
        _clicks.Clear();
        _turn = 0;
        ClearFit();
        _filling = true;
        KindBox.SelectedIndex = Array.IndexOf(Kinds, capture.Kind);
        _filling = false;

        ScanInfo.Text = $"{capture.Name} · {_img.Width} × {_img.Height}" +
                        (_img.Dpi is { } d ? $" · {d:0} dpi" : "") + $"\nFrom: {capture.Source}";
        string warn = "";
        if (capture.Kind == CaptureKind.FlatbedScan && _img.Dpi is { } dpi && dpi < 400) warn += " Below 400 dpi: rescan at 600 if you can.";
        if (capture.FilePath is { } p && (p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
            warn += " JPEG compression changes colours: PNG or TIFF is better.";
        StatusText.Text = "Image loaded." + warn;

        if (engravedAt is { } known && known.Rows == GridRows && known.Columns == GridColumns)
        {
            // Straight onto where the job was engraved: no searching, nothing to line up.
            _manual = false;
            _detection = null;
            _placedFromJob = true;
            _turn = TurnOf(known);
            ApplyGrid(new GridFitResult(known, 0, 0));
            StatusText.Text = "The grid is where the job was engraved. Check the boxes sit on the squares, then add the results.";
            UpdateSteps();
            Redraw();
            ZoomToGrid();
            return;
        }
        if (clicks is { Count: 3 })
        {
            _manual = true;
            _clicks.AddRange(clicks);
            Fit();
        }
        else
        {
            _manual = false;
            await FindAsync();
        }
        UpdateSteps();
        Redraw();
    }

    private async Task PasteAsync()
    {
        if (Clipboard is not { } cb) return;
        try
        {
            // A copied file (Explorer, Finder) first, then picture data (screenshots, browsers, phone apps).
            if (await cb.TryGetFilesAsync() is { } items &&
                items.Select(i => i.TryGetLocalPath()).FirstOrDefault(IsImageFile) is { } file)
            {
                await LoadAsync(file, source: "Pasted file");
                return;
            }
            if (await cb.TryGetBitmapAsync() is { } bmp)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms); // PNG: lossless
                var img = await Task.Run(() => RgbImage.Decode(ms.ToArray()));
                await UseAsync(Keep(new CardCapture(img, CaptureKind.Photo, "Clipboard", "pasted image"), "pasted"));
                return;
            }
            StatusText.Text = "The clipboard has no picture in it. Copy an image or an image file, then paste.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not paste that picture ({ex.Message}). Save it to a file and open it instead.";
        }
    }

    private static bool IsImageFile(string? path) =>
        path is not null && ScanFolderWatcher.ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(IsImageFile) is { } path)
            await LoadAsync(path, source: "Dropped file");
        else StatusText.Text = "That is not an image file Atlas can read (PNG, TIFF, JPEG, BMP).";
    }

    private async Task ScanAsync()
    {
        if (!ScannerCapture.IsSupported) { StatusText.Text = ScannerCapture.Unsupported; return; }
        ScanButton.IsEnabled = false;
        StatusText.Text = "Waiting for the scanner…";
        try
        {
            var folder = Directory.Exists(_settings.WatchFolder ?? "") ? _settings.WatchFolder! : CaptureSettings.CapturesFolder;
            if (await ScannerCapture.ScanAsync(folder) is { } path)
                await LoadAsync(path, kind: CaptureKind.FlatbedScan, source: "Scanner");
            else StatusText.Text = "Scan cancelled.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not scan: {ex.Message}"; }
        finally { ScanButton.IsEnabled = true; }
    }

    private async Task CameraAsync()
    {
        var result = await new CameraWindow(_settings).ShowDialog<CameraResult?>(this);
        if (result is null) return;
        var c = new CardCapture(result.Image, CaptureKind.Photo, $"USB camera: {result.CameraName}, {result.Format}, {result.Frames} frames averaged", "camera");
        await UseAsync(Keep(c, "camera"));
    }

    private async Task LumosAsync()
    {
        LumosButton.IsEnabled = false;
        StatusText.Text = $"Taking a picture with the laser's camera ({_settings.LumosAddress})…";
        try
        {
            using var client = new LumosUltra.Client.LumosUltraClient(_settings.LumosAddress);
            var bytes = await client.TakePhotoAsync();
            // The Ultra's camera delivers its picture on its side; turned upright as set (turn/mirror buttons).
            var img = await Task.Run(() => _settings.LumosUpright(bytes));
            await UseAsync(Keep(new CardCapture(img, CaptureKind.Photo, "Lumos Ultra camera", "lumos"), "lumos"));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not reach the laser's camera at {_settings.LumosAddress}: {ex.Message}";
        }
        finally { LumosButton.IsEnabled = true; }
    }

    // ---------------------------------------------------------------- scan folder

    private async Task ChooseWatchFolderAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder your scanner saves into",
            AllowMultiple = false,
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } dir) return;
        _settings.WatchFolder = dir;
        WatchBox.IsChecked = true; // also restarts the watcher through IsCheckedChanged
        UpdateWatcher();
    }

    private void UpdateWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        bool on = WatchBox.IsChecked == true && Directory.Exists(_settings.WatchFolder ?? "");
        _settings.WatchEnabled = WatchBox.IsChecked == true;
        _settings.Save();
        if (!on)
        {
            WatchText.Text = _settings.WatchFolder is { } f ? $"Scan folder: {f}" : "No scan folder chosen.";
            return;
        }
        try
        {
            _watcher = new ScanFolderWatcher(_settings.WatchFolder!);
            _watcher.ImageArrived += path => Dispatcher.UIThread.Post(async () =>
                await LoadAsync(path, kind: CaptureKind.FlatbedScan, source: $"Scan folder ({Path.GetFileName(_settings.WatchFolder)})"));
            WatchText.Text = $"Watching {_settings.WatchFolder}: scan the card and it opens here.";
        }
        catch (Exception ex) { WatchText.Text = $"Cannot watch that folder: {ex.Message}"; }
    }

    private void SetViewLevel(int stops, bool stretch)
    {
        _viewStops = Math.Clamp(stops, -4, 3);
        _viewStretch = stretch;
        RefreshPicture();
    }

    /// <summary>Redraws the picture with the view level; the image itself (and every measurement) is untouched.</summary>
    private void RefreshPicture()
    {
        ViewLevelText.Text = _viewStops == 0 && !_viewStretch ? "" : $"View only: {(_viewStops == 0 ? "" : $"{_viewStops:+0;-0} stop{(Math.Abs(_viewStops) == 1 ? "" : "s")}")}{(_viewStops != 0 && _viewStretch ? ", " : "")}{(_viewStretch ? "contrast stretched" : "")}";
        if (_img is null) return;
        (ScanImage.Source as IDisposable)?.Dispose();
        ScanImage.Source = ToBitmap(_img, ViewLut(_img, _viewStops, _viewStretch));
    }

    /// <summary>
    /// A per-channel lookup table for showing the picture: optionally stretch each channel's
    /// 0.5-99.5 % range to full scale, then change exposure in linear light by whole stops.
    /// </summary>
    private static byte[][] ViewLut(RgbImage img, int stops, bool stretch)
    {
        var luts = new byte[3][];
        double gain = Math.Pow(2, stops);
        for (int ch = 0; ch < 3; ch++)
        {
            double lo = 0, hi = 255;
            if (stretch)
            {
                var hist = new long[256];
                long n = 0;
                for (int i = ch; i < img.Pixels.Length; i += 3 * 7) { hist[img.Pixels[i]]++; n++; }
                long acc = 0;
                lo = Array.FindIndex(hist, h => (acc += h) > n * 0.005);
                acc = 0;
                for (int v = 255; v >= 0; v--) { acc += hist[v]; if (acc > n * 0.005) { hi = v; break; } }
                if (hi - lo < 8) { lo = 0; hi = 255; }
            }
            var lut = new byte[256];
            for (int v = 0; v < 256; v++)
            {
                double x = Math.Clamp((v - lo) / (hi - lo), 0, 1);
                double lin = x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
                lin = Math.Min(1, lin * gain);
                double o = lin <= 0.0031308 ? lin * 12.92 : 1.055 * Math.Pow(lin, 1 / 2.4) - 0.055;
                lut[v] = (byte)Math.Round(Math.Clamp(o, 0, 1) * 255);
            }
            luts[ch] = lut;
        }
        return luts;
    }

    private static WriteableBitmap ToBitmap(RgbImage img, byte[][]? lut = null)
    {
        lut ??= [Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(), Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(), Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()];
        var wb = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96),
                                     PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = wb.Lock();
        var row = new byte[img.Width * 4];
        for (int y = 0; y < img.Height; y++)
        {
            int s = y * img.Width * 3;
            for (int x = 0; x < img.Width; x++)
            {
                row[x * 4] = lut[2][img.Pixels[s + x * 3 + 2]];
                row[x * 4 + 1] = lut[1][img.Pixels[s + x * 3 + 1]];
                row[x * 4 + 2] = lut[0][img.Pixels[s + x * 3]];
                row[x * 4 + 3] = 255;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
        }
        return wb;
    }

    // ---------------------------------------------------------------- clicks and fit

    /// <summary>
    /// Scale and offset of the picture in the view: at zoom 1 the whole picture fits; zoomed,
    /// <see cref="_centre"/> (an image point) sits in the middle of the view.
    /// </summary>
    private (double scale, double ox, double oy) View()
    {
        if (_img is null) return (1, 0, 0);
        double w = ViewHost.Bounds.Width, h = ViewHost.Bounds.Height;
        double s = Math.Min(w / _img.Width, h / _img.Height) * _zoom;
        var c = _centre ?? new PointD(_img.Width / 2.0, _img.Height / 2.0);
        return (s, w / 2 - c.X * s, h / 2 - c.Y * s);
    }

    private PointD ToImage(Point p)
    {
        var (s, ox, oy) = View();
        return new PointD((p.X - ox) / s, (p.Y - oy) / s);
    }

    private Point ToScreen(PointD p)
    {
        var (s, ox, oy) = View();
        return new Point(ox + p.X * s, oy + p.Y * s);
    }

    /// <summary>Zooms by a factor, keeping the picture under <paramref name="at"/> (or the view centre) still.</summary>
    private void ZoomBy(double factor, Point? at)
    {
        if (_img is null) return;
        var anchor = at ?? new Point(ViewHost.Bounds.Width / 2, ViewHost.Bounds.Height / 2);
        var ip = ToImage(anchor);
        _zoom = Math.Clamp(_zoom * factor, 0.5, 40);
        double s = View().scale;
        _centre = new PointD(ip.X - (anchor.X - ViewHost.Bounds.Width / 2) / s, ip.Y - (anchor.Y - ViewHost.Bounds.Height / 2) / s);
        Redraw();
    }

    /// <summary>Fills the view with the grid and a little of the card around it.</summary>
    private void ZoomToGrid()
    {
        if (_img is null || (_dragGrid ?? _fit?.Grid) is not { } g) return;
        var pts = Corners(g).ToList();
        double pad = g.Pitch * 1.6;
        double x0 = pts.Min(p => p.X) - pad, x1 = pts.Max(p => p.X) + pad, y0 = pts.Min(p => p.Y) - pad, y1 = pts.Max(p => p.Y) + pad;
        double w = ViewHost.Bounds.Width, h = ViewHost.Bounds.Height;
        double fit = Math.Min(w / _img.Width, h / _img.Height);
        _zoom = Math.Clamp(Math.Min(w / (x1 - x0), h / (y1 - y0)) / fit, 0.5, 40);
        _centre = new PointD((x0 + x1) / 2, (y0 + y1) / 2);
        Redraw();
    }

    /// <summary>Which way up the card lies in the picture: the direction its rows run, to the nearest quarter turn.</summary>
    private static int TurnOf(GridGeometry g)
    {
        var a = g.Centre(0, 0);
        var b = g.Centre(0, g.Columns - 1);
        double deg = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
        return ((int)Math.Round(deg / 90) * 90 % 360 + 360) % 360;
    }

    /// <summary>The centres of the four corner squares: top-left, top-right, bottom-left, bottom-right (card order).</summary>
    private static IEnumerable<PointD> Corners(GridGeometry g)
    {
        yield return g.Centre(0, 0);
        yield return g.Centre(0, g.Columns - 1);
        yield return g.Centre(g.Rows - 1, 0);
        yield return g.Centre(g.Rows - 1, g.Columns - 1);
    }

    private static GridGeometry Rewarp(Homography warp, GridGeometry old) =>
        old.IsPerspective || old.Warp is not null
            ? GridGeometry.FromWarp(warp, old.Rows, old.Columns)
            : new GridGeometry(warp.Map(0, 0), warp.Map(1, 0) - warp.Map(0, 0), warp.Map(0, 1) - warp.Map(0, 0), old.Rows, old.Columns);

    /// <summary>Which square (column, row, fractional) an image point falls in, or null when off the grid.</summary>
    private static (double col, double row)? CellAt(GridGeometry g, PointD ip)
    {
        if (g.AsWarp().Inverse() is not { } inv) return null;
        var q = inv.Map(ip);
        return q.X >= -0.5 && q.Y >= -0.5 && q.X <= g.Columns - 0.5 && q.Y <= g.Rows - 0.5 ? (q.X, q.Y) : null;
    }

    private void OnViewPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_img is null) return;
        ViewHost.Focus();
        var pt = e.GetCurrentPoint(ViewHost);
        _dragScreen = pt.Position;
        _dragImage = ToImage(pt.Position);
        var grid = _fit?.Grid;
        if (!pt.Properties.IsLeftButtonPressed) _drag = DragKind.Pan;
        else if (_cropMode) { _drag = DragKind.Crop; _cropEnd = pt.Position; }
        else if (grid is not null && Corners(grid).Select((c, i) => (d: Distance(ToScreen(c), pt.Position), i)).MinBy(x => x.d) is { d: < 14 } hit)
        {
            _drag = DragKind.Corner;
            _dragCorner = hit.i;
        }
        else if (grid is not null && CellAt(grid, _dragImage) is not null) _drag = DragKind.Grid;
        else _drag = DragKind.Pan;
        _dragStartGrid = grid;
        _panCentre = _centre ?? new PointD(_img.Width / 2.0, _img.Height / 2.0);
        e.Pointer.Capture(ViewHost);
        e.Handled = true;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private void OnViewMoved(object? sender, PointerEventArgs e)
    {
        if (_img is null) return;
        var p = e.GetPosition(ViewHost);
        switch (_drag)
        {
            case DragKind.Pan:
                double s = View().scale;
                _centre = new PointD(_panCentre.X - (p.X - _dragScreen.X) / s, _panCentre.Y - (p.Y - _dragScreen.Y) / s);
                Redraw();
                break;
            case DragKind.Grid when _dragStartGrid is { } g:
                var d = ToImage(p) - _dragImage;
                _dragGrid = Rewarp(g.AsWarp().Then(Homography.Affine(d, new PointD(1, 0), new PointD(0, 1))), g);
                Redraw();
                break;
            case DragKind.Corner when _dragStartGrid is { } g:
                var dst = Corners(g).ToArray();
                dst[_dragCorner] = ToImage(p);
                PointD[] src = [new(0, 0), new(g.Columns - 1, 0), new(0, g.Rows - 1), new(g.Columns - 1, g.Rows - 1)];
                if (Homography.Fit(src, dst) is { } h) _dragGrid = GridGeometry.FromWarp(h, g.Rows, g.Columns);
                Redraw();
                break;
            case DragKind.Crop:
                _cropEnd = p;
                Redraw();
                break;
            default:
                // Not dragging: say which square is under the pointer.
                if (_fit is { } fit && _measure is { } m && _outcomes is { } oc && CellAt(fit.Grid, ToImage(p)) is { } q)
                {
                    int r = (int)Math.Round(q.row), c = (int)Math.Round(q.col);
                    HoverText.Text = Describe(m.At(r, c), oc[r, c]);
                }
                ViewHost.Cursor = new Cursor(_cropMode ? StandardCursorType.Cross
                    : _fit is { } f2 && Corners(f2.Grid).Any(c => Distance(ToScreen(c), p) < 14) ? StandardCursorType.SizeAll
                    : _fit is { } f3 && CellAt(f3.Grid, ToImage(p)) is not null ? StandardCursorType.Hand
                    : StandardCursorType.Arrow);
                break;
        }
    }

    private async void OnViewReleased(object? sender, PointerReleasedEventArgs e)
    {
        var kind = _drag;
        _drag = DragKind.None;
        e.Pointer.Capture(null);
        if (_img is null) return;
        if (kind is DragKind.Grid or DragKind.Corner && _dragGrid is { } moved)
        {
            _dragGrid = null;
            if (Distance(e.GetPosition(ViewHost), _dragScreen) < 3) { Redraw(); return; } // a click, not a drag
            // Let go: the grid stays exactly where it was dropped.
            PushHistory();
            _manual = true;
            _clicks.Clear();
            ApplyGrid(new GridFitResult(moved, 0, 0), hand: true);
            UpdateSteps();
            Redraw();
        }
        else if (kind == DragKind.Crop && _cropEnd is { } end)
        {
            _cropEnd = null;
            SetCropMode(false);
            await CropAsync(_dragImage, ToImage(end));
        }
    }

    private void OnViewWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_img is null) return;
        ZoomBy(Math.Pow(1.25, e.Delta.Y), e.GetPosition(ViewHost));
        e.Handled = true;
    }

    /// <summary>Arrow keys move the grid one square, unless typing in a box.</summary>
    private void OnArrowKey(object? sender, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox or NumericUpDown or ComboBox) return;
        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Undo(); e.Handled = true; return; }
        if (_fit is null) return;
        PointD? dir = e.Key switch
        {
            Key.Left => new PointD(-1, 0), Key.Right => new PointD(1, 0),
            Key.Up => new PointD(0, -1), Key.Down => new PointD(0, 1), _ => null,
        };
        if (dir is { } d) { Move(d); e.Handled = true; }
    }

    // ---------------------------------------------------------------- crop

    private void SetCropMode(bool on)
    {
        _cropMode = on && _img is not null;
        CropButton.Content = _cropMode ? "Cancel crop" : "Crop to card";
        if (_cropMode) StatusText.Text = "Drag a box around the card on the picture.";
        _cropEnd = null;
        Redraw();
    }

    private async Task CropAsync(PointD a, PointD b)
    {
        if (_img is null || _capture is null) return;
        int x0 = (int)Math.Clamp(Math.Min(a.X, b.X), 0, _img.Width), x1 = (int)Math.Clamp(Math.Max(a.X, b.X), 0, _img.Width);
        int y0 = (int)Math.Clamp(Math.Min(a.Y, b.Y), 0, _img.Height), y1 = (int)Math.Clamp(Math.Max(a.Y, b.Y), 0, _img.Height);
        int w = x1 - x0, h = y1 - y0;
        if (w < 40 || h < 40) { StatusText.Text = "That box is too small: drag a box around the whole card."; return; }
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(_img.Pixels, ((y0 + y) * _img.Width + x0) * 3, px, y * w * 3, w * 3);
        var cropped = new RgbImage(w, h, px) { Dpi = _img.Dpi };
        _uncropped ??= _capture;
        _cropOffset = new PointD(_cropOffset.X + x0, _cropOffset.Y + y0);
        var c = _capture with
        {
            Image = cropped,
            Source = $"{_uncropped.Source}; cropped to {w} × {h} px at {_cropOffset.X:0},{_cropOffset.Y:0}",
        };
        await UseAsync(c, cropped: true);
    }

    /// <summary>
    /// Turns or mirrors the picture (and the whole picture behind a crop). For the laser's
    /// camera the choice is remembered, so its next pictures come in the same way up.
    /// </summary>
    private async Task ReorientAsync(bool turn)
    {
        if (_capture is null) return;
        RgbImage Apply(RgbImage i) => turn ? i.RotateClockwise() : i.MirrorHorizontal();
        var whole = _uncropped ?? _capture;
        if (whole.Source.StartsWith("Lumos Ultra camera", StringComparison.Ordinal))
        {
            // Picture = mirror^m · turn^t; turning after a mirror counts as a turn the other way.
            if (turn) _settings.LumosQuarterTurns += _settings.LumosMirror ? -1 : 1;
            else _settings.LumosMirror = !_settings.LumosMirror;
            _settings.LumosQuarterTurns = ((_settings.LumosQuarterTurns % 4) + 4) % 4;
            _settings.Save();
        }
        // A turned picture no longer matches the crop, so go back to the whole picture.
        var img = await Task.Run(() => Apply(whole.Image));
        await UseAsync(whole with { Image = img });
    }

    private async Task UndoCropAsync()
    {
        if (_uncropped is not { } whole) return;
        await UseAsync(whole);
    }

    /// <summary>
    /// A grid in the middle of the view for the user to drag into place: move it by dragging
    /// inside it, reshape it by its corner handles. It snaps to the squares when let go.
    /// </summary>
    private void PlaceByHand()
    {
        if (_img is null) return;
        int rows = GridRows, cols = GridColumns;
        if (rows < 2 || cols < 2) return;
        var tl = ToImage(new Point(0, 0));
        var br = ToImage(new Point(ViewHost.Bounds.Width, ViewHost.Bounds.Height));
        double vw = Math.Min(br.X, _img.Width) - Math.Max(tl.X, 0), vh = Math.Min(br.Y, _img.Height) - Math.Max(tl.Y, 0);
        var mid = new PointD((Math.Max(tl.X, 0) + Math.Min(br.X, _img.Width)) / 2, (Math.Max(tl.Y, 0) + Math.Min(br.Y, _img.Height)) / 2);
        double pitch = Math.Min(0.6 * vw / cols, 0.6 * vh / rows);
        var origin = new PointD(mid.X - (cols - 1) / 2.0 * pitch, mid.Y - (rows - 1) / 2.0 * pitch);
        _findCts?.Cancel();
        PushHistory();
        _manual = true;
        _clicks.Clear();
        _detection = null;
        _turn = 0;
        ApplyGrid(new GridFitResult(new GridGeometry(origin, new PointD(pitch, 0), new PointD(0, pitch), rows, cols), 0, 0), hand: true);
        StatusText.Text = "Place the grid of boxes over the engraved squares by hand: see the steps on the left.";
        UpdateSteps();
        Redraw();
    }

    /// <summary>
    /// Finds the grid without clicks. For an Atlas grid with a number, a picture showing several
    /// grids (the laser camera's or a webcam's view of a whole sheet) is searched for every grid
    /// of that shape and the one whose engraved number matches is used.
    /// </summary>
    private static GridDetection? FindGrid(RgbImage img, int rows, int cols, CardMarks marks, TestGrid? existing)
    {
        if (existing is { Number: > 0 } g && CardId.For(g.Layout) is not null && rows == g.Layout.Rows && cols == g.Layout.Columns)
        {
            var all = GridDetect.DetectEach(img, rows, cols, marks);
            if (all.FirstOrDefault(d => CardId.Read(img, d.Grid, g.Layout) == g.Number) is { } mine) return mine;
            // No number read (blurred or too small in the picture): use a single certain find.
            var certain = all.Where(d => d.PlacementCertain).ToList();
            if (certain.Count == 1 && certain.All(d => CardId.Read(img, d.Grid, g.Layout) is null)) return certain[0];
            if (all.Count > 0) return null; // other grids only: do not put this grid's results on them
        }
        return GridDetect.Detect(img, rows, cols, marks: marks);
    }

    private void UpdateSteps()
    {
        if (_img is null) { StepText.Text = "Bring in an image of the card first."; return; }
        if (_fit is null && !_manual) { StepText.Text = "Looking for the squares…"; return; }
        string corners = $"Top-left: {DescribeCellAt(0, 0)}\nTop-right: {DescribeCellAt(0, GridColumns - 1)}\n" +
                         $"Bottom-left: {DescribeCellAt(GridRows - 1, 0)}";
        StepText.Text = _placedFromJob
            ? "Placed where the job was engraved. Check each box sits on a square; if one is slightly off, Snap to squares or drag a corner handle."
            : !_manual
            ? "Found automatically. Check each box sits on a square. If not, drag the grid, or drag a round corner handle onto its corner square."
            : "Placing the grid by hand. The boxes drawn over the picture are where Atlas will read each cell's colour; " +
              "they have to sit on the engraved squares.\n\n" +
              "1. Move it: press inside the grid of boxes (not on a round handle) and drag it over the card, so the boxes roughly cover the squares.\n" +
              "2. Fit it: each corner box has a round handle. Drag the top-left handle onto the middle of the card's top-left square, " +
              "then the top-right and bottom-left handles onto theirs (bottom-right follows).\n" +
              "3. Check: scroll to zoom in; every box should sit on a square. Snap to squares can fine-tune.\n" +
              "Blank squares count too: the grid's top-left corner is the top-left cell even if it left no mark.\n\n" +
              $"The corner cells are\n{corners}";
    }

    private void ClearFit()
    {
        _fit = null;
        _detection = null;
        _measure = null;
        _outcomes = null;
        FitText.Text = "";
        SummaryText.Text = "";
        HoverText.Text = "";
        Swatches.Children.Clear();
        SaveButton.IsEnabled = false;
    }

    /// <summary>Finds the squares with no clicks (see <see cref="GridDetect"/>).</summary>
    private async Task FindAsync(bool debounce = false)
    {
        if (_img is null) return;
        _findCts?.Cancel();
        var cts = _findCts = new CancellationTokenSource();
        if (CardProblem is { } problem) { ClearFit(); StatusText.Text = problem; return; }
        int rows = GridRows, cols = GridColumns;
        var marks = Marks;
        _manual = false;
        _clicks.Clear();
        try
        {
            if (debounce) await Task.Delay(400, cts.Token); // typing a row count fires once per key
            StatusText.Text = "Looking for the squares…";
            UpdateSteps();
            var img = _img;
            var existing = _ctx.Existing;
            var det = await Task.Run(() => FindGrid(img, rows, cols, marks, existing), cts.Token);
            if (cts.IsCancellationRequested || img != _img) return;
            if (det is null)
            {
                if (_fit is null) PlaceByHand(); // keep a grid the user already placed
                _manual = true;
                UpdateSteps();
                StatusText.Text = (existing is { Number: > 0 } ? $"Could not find grid {existing.Number} in this picture by itself" : $"Could not find a {rows} × {cols} grid of squares by itself") +
                                  ", so you place it by hand: see the steps on the left. (Often it can find the grid after Crop to card: draw a box round just this card, then Find automatically.)";
                return;
            }
            PushHistory();
            _detection = det;
            _turn = det.TurnDegrees;
            ApplyGrid(new GridFitResult(det.Grid, det.RmsPixels, det.CellsUsed));
            if (_history.Count > 0) StatusText.Text = "Found automatically. If it is wrong, Undo puts your grid back.";
            // A small card in a big picture (the laser camera's whole bed): zoom in on it.
            var span = Corners(det.Grid).ToList();
            if (_zoom == 1 && span.Max(p => p.X) - span.Min(p => p.X) < img.Width * 0.45 && span.Max(p => p.Y) - span.Min(p => p.Y) < img.Height * 0.45)
                ZoomToGrid();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_fit is null) PlaceByHand();
            _manual = true;
            StatusText.Text = $"Could not search the image: {ex.Message}. Drag the grid into place.";
        }
        UpdateSteps();
        Redraw();
    }

    /// <summary>
    /// Moves the boxes one square in a direction on screen. The card may be turned in the
    /// picture, so the card step (next column or row, either way) that moves the boxes most
    /// nearly that way is used.
    /// </summary>
    private void Move(PointD screenDirection)
    {
        if (_fit is null || _img is null) return;
        var g = _fit.Grid;
        int rc = (g.Rows - 1) / 2, cc = (g.Columns - 1) / 2;
        var colStep = g.ColStepAt(rc, cc);
        var rowStep = g.RowStepAt(rc, cc);
        static double Along(PointD v, PointD dir) => (v.X * dir.X + v.Y * dir.Y) / Math.Max(v.Length, 1e-9);
        var (dc, dr) = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }
            .MaxBy(s => Along(colStep * s.Item1 + rowStep * s.Item2, screenDirection));
        // Card cell (c, r) now sits where (c + dc, r + dr) was: every box moves one step that way.
        var warp = Homography.Affine(new PointD(dc, dr), new PointD(1, 0), new PointD(0, 1)).Then(g.AsWarp());
        Reapply(warp, g);
    }

    /// <summary>The card is the other way up: first row ↔ last row, first column ↔ last column.</summary>
    private void TurnHalf()
    {
        if (_fit is null || _img is null) return;
        var g = _fit.Grid;
        var warp = Homography.Affine(new PointD(g.Columns - 1, g.Rows - 1), new PointD(-1, 0), new PointD(0, -1)).Then(g.AsWarp());
        _turn = (_turn + 180) % 360;
        Reapply(warp, g);
    }

    private void Reapply(Homography warp, GridGeometry old)
    {
        PushHistory();
        _manual = true;
        ApplyGrid(new GridFitResult(Rewarp(warp, old), 0, 0), hand: true);
        UpdateSteps();
        Redraw();
    }

    private void PushHistory()
    {
        _placedFromJob = false;
        if (_fit is null) return;
        _history.Push((_fit, _detection, _turn, _manual, _handPlaced));
        UndoButton.IsEnabled = true;
    }

    private void Undo()
    {
        if (_history.Count == 0 || _img is null) return;
        _findCts?.Cancel();
        var (fit, det, turn, manual, hand) = _history.Pop();
        _detection = det;
        _turn = turn;
        _manual = manual;
        ApplyGrid(fit, hand);
        UndoButton.IsEnabled = _history.Count > 0;
        StatusText.Text = "Grid put back.";
        UpdateSteps();
        Redraw();
    }

    /// <summary>
    /// Fine-tunes a hand-placed grid onto the square centres nearby - only when asked, and only
    /// if that keeps every corner within a third of a square of where the user put it.
    /// </summary>
    private void Snap()
    {
        if (_img is null || _fit is null) return;
        var placed = _fit.Grid;
        var snapped = GridFit.Refine(_img, placed, squaresDarker: _detection?.SquaresDarker ?? false);
        double worst = Corners(placed).Zip(Corners(snapped.Grid), (a, b) => (a - b).Length).Max();
        if (snapped.CellsUsed < 4 || worst > placed.Pitch / 3)
        {
            StatusText.Text = "Snapping would pull the grid away from where you put it (the squares are hard to make out in this picture), so it stays as you placed it.";
            return;
        }
        PushHistory();
        ApplyGrid(snapped);
        StatusText.Text = $"Snapped: the corners moved at most {worst:0.0} px. Undo if you preferred your placement.";
        Redraw();
    }

    /// <summary>Measures every cell of a placed grid and shows the results.</summary>
    private void ApplyGrid(GridFitResult fit, bool hand = false)
    {
        if (_img is null) return;
        int rows = GridRows, cols = GridColumns;
        _fit = fit;
        _handPlaced = hand;
        _measure = ScanMeasure.Measure(_img, fit.Grid);
        _outcomes = new Outcome[rows, cols];
        foreach (var c in _measure.Cells) _outcomes[c.Row, c.Column] = c.Suggested;

        bool good = fit.CellsUsed >= Math.Max(6, rows * cols / 10) && fit.RmsPixels < fit.Grid.Pitch * 0.06
                    && (_detection?.PlacementCertain ?? true);
        var lines = new List<string>
        {
            _placedFromJob
                ? $"Placed where the job was engraved (from the camera calibration). Snap to squares can fine-tune it. Bare metal {_measure.Bare}."
                : hand
                ? $"Placed by hand: each square is measured exactly where its box is. Bare metal {_measure.Bare}."
                : $"Found automatically: fitted to {fit.CellsUsed} squares, accurate to {fit.RmsPixels:0.0} px; bare metal {_measure.Bare}.",
        };
        if (_turn != 0) lines.Add($"The card is {GridDetect.DescribeTurn(_turn)}. Colours read at another angle to the light are not comparable with standard scans.");
        if (fit.Grid.IsPerspective) lines.Add("Photographed at an angle: perspective corrected.");
        if (_detection is { } det && !hand && !_placedFromJob) lines.AddRange(det.Notes.Where(n => !n.StartsWith("The card is") && !n.StartsWith("Photographed")));
        if (!good && !hand && !_placedFromJob) lines.Add("The fit is uncertain: check the boxes sit on the squares.");
        // The card's own ID, if it has one: catches reading the wrong grid.
        if (_ctx.Existing is { } eg && CardId.Read(_img, fit.Grid, eg.Layout) is { } cardNumber)
            lines.Insert(0, cardNumber == eg.Number
                ? $"Card ID reads grid {cardNumber}: the right card."
                : $"⚠ This card's ID reads grid {cardNumber}, but you are reading grid {eg.Number}. Check you have the right card.");
        FitText.Text = string.Join("\n", lines);
        SaveButton.IsEnabled = true;
        StatusText.Text = "Check the boxes and the suggested results, then add the card to the atlas.";
        DrawSwatches();
    }

    private void Fit()
    {
        if (_img is null || _clicks.Count < 3) return;
        if (CardProblem is { } problem)
        {
            ClearFit();
            StatusText.Text = problem;
            return;
        }
        try
        {
            var start = GridGeometry.FromCorners(_clicks[0], _clicks[1], _clicks[2], GridRows, GridColumns);
            _detection = null;
            _turn = 0; // clicked in the card's own order: top-left, top-right, bottom-left
            ApplyGrid(GridFit.Refine(_img, start));
        }
        catch (Exception ex)
        {
            ClearFit();
            StatusText.Text = $"Could not fit the grid: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- drawing

    private void Redraw()
    {
        Overlay.Children.Clear();
        if (_img is null) return;
        var (s, ox, oy) = View();
        ScanImage.Width = _img.Width * s;
        ScanImage.Height = _img.Height * s;
        Canvas.SetLeft(ScanImage, ox);
        Canvas.SetTop(ScanImage, oy);
        // Sharp pixels when zoomed in close, smooth when the picture is shrunk.
        RenderOptions.SetBitmapInterpolationMode(ScanImage, s >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);

        var grid = _dragGrid ?? _fit?.Grid;
        if (grid is not null)
        {
            bool live = _dragGrid is not null;
            for (int r = 0; r < grid.Rows; r++)
                for (int c = 0; c < grid.Columns; c++)
                {
                    double h = grid.PitchAt(r, c) * 0.2 * s;
                    var p = grid.Centre(r, c);
                    var brush = live || _outcomes is not { } oc ? Brushes.White : OutcomeBrush(oc[r, c]);
                    var box = new Border { Width = 2 * h, Height = 2 * h, BorderBrush = brush, BorderThickness = new Thickness(1.5) };
                    Canvas.SetLeft(box, ox + p.X * s - h);
                    Canvas.SetTop(box, oy + p.Y * s - h);
                    Overlay.Children.Add(box);
                }

            // The grid's outline, and a round handle on each corner square to drag.
            var w = grid.AsWarp();
            PointD[] edge = [new(-0.5, -0.5), new(grid.Columns - 0.5, -0.5), new(grid.Columns - 0.5, grid.Rows - 0.5), new(-0.5, grid.Rows - 0.5)];
            var outline = new Avalonia.Controls.Shapes.Polygon
            {
                Points = edge.Select(e => { var q = w.Map(e); return new Point(ox + q.X * s, oy + q.Y * s); }).ToList(),
                Stroke = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), StrokeThickness = 1, StrokeDashArray = [4, 3],
            };
            Overlay.Children.Add(outline);
            foreach (var corner in Corners(grid))
            {
                var handle = Handle();
                Canvas.SetLeft(handle, ox + corner.X * s - 8);
                Canvas.SetTop(handle, oy + corner.Y * s - 8);
                Overlay.Children.Add(handle);
            }
        }

        if (_drag == DragKind.Crop && _cropEnd is { } end)
        {
            var a = _dragScreen;
            var rect = new Border
            {
                Width = Math.Abs(end.X - a.X), Height = Math.Abs(end.Y - a.Y),
                BorderBrush = Brushes.Yellow, BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 0)),
            };
            Canvas.SetLeft(rect, Math.Min(a.X, end.X));
            Canvas.SetTop(rect, Math.Min(a.Y, end.Y));
            Overlay.Children.Add(rect);
        }

        foreach (var (p, i) in _clicks.Select((p, i) => (p, i)))
        {
            var dot = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(7),
                Background = Brushes.Yellow, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 9, Foreground = Brushes.Black,
                                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            };
            Canvas.SetLeft(dot, ox + p.X * s - 7);
            Canvas.SetTop(dot, oy + p.Y * s - 7);
            Overlay.Children.Add(dot);
        }
    }

    /// <summary>A corner handle: a yellow ring that shows on light and dark cards alike.</summary>
    private static Avalonia.Controls.Shapes.Ellipse Handle() => new()
    {
        Width = 16, Height = 16,
        Stroke = Brushes.Yellow, StrokeThickness = 2.5,
        Fill = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
    };

    private static IBrush OutcomeBrush(Outcome o) => o switch
    {
        Outcome.NoMark => Brushes.DeepSkyBlue,
        Outcome.Uneven => Brushes.Gold,
        Outcome.Damaged => Brushes.OrangeRed,
        _ => Brushes.Magenta,
    };

    private void DrawSwatches()
    {
        Swatches.Children.Clear();
        if (_measure is not { } m || _outcomes is not { } oc) return;
        int rows = GridRows, cols = GridColumns;

        double size = Math.Floor(Math.Min(300.0 / cols, 22));
        Swatches.Width = size * cols;
        Swatches.Height = size * rows;

        foreach (var c in m.Cells)
        {
            var o = oc[c.Row, c.Column];
            var sw = new Border
            {
                Width = size - 1, Height = size - 1,
                Background = new SolidColorBrush(Color.FromRgb(c.Rgb.R, c.Rgb.G, c.Rgb.B)),
                BorderBrush = o == Outcome.Good ? null : OutcomeBrush(o),
                BorderThickness = new Thickness(o == Outcome.Good ? 0 : 2),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var cell = c;
            ToolTip.SetTip(sw, Describe(cell, o));
            sw.PointerEntered += (_, _) => HoverText.Text = Describe(cell, oc[cell.Row, cell.Column]);
            sw.PointerPressed += (_, _) =>
            {
                int k = Array.IndexOf(Cycle, oc[cell.Row, cell.Column]);
                oc[cell.Row, cell.Column] = Cycle[(k + 1) % Cycle.Length];
                DrawSwatches();
                Redraw();
                HoverText.Text = Describe(cell, oc[cell.Row, cell.Column]);
            };
            Canvas.SetLeft(sw, c.Column * size);
            Canvas.SetTop(sw, c.Row * size);
            Swatches.Children.Add(sw);
        }

        var counts = Enum.GetValues<Outcome>().Select(o => (o, n: m.Cells.Count(c => oc[c.Row, c.Column] == o)))
                         .Where(x => x.n > 0).Select(x => $"{x.n} {Label(x.o)}");
        if (_ctx.Existing is not null)
        {
            SummaryText.Text = $"{m.Cells.Count} squares: {string.Join(", ", counts)}.\nLaid out as on the card, A1 at top left.";
            return;
        }
        var d = Definition();
        SummaryText.Text = $"{m.Cells.Count} squares: {string.Join(", ", counts)}.\n" +
                           $"Laid out as on the card: rows {ColorTestDefinition.Name(d.RowSweep.Axis).ToLowerInvariant()} " +
                           $"({d.RowSweep.First:0.##} at top), columns {ColorTestDefinition.Name(d.ColumnSweep.Axis).ToLowerInvariant()} " +
                           $"({d.ColumnSweep.First:0.##} at left).";
    }

    private static string Label(Outcome o) => o switch
    {
        Outcome.NoMark => "no mark",
        Outcome.Good => "good",
        Outcome.Uneven => "uneven",
        Outcome.Damaged => "damaged",
        _ => o.ToString(),
    };

    private string Describe(CellReading c, Outcome o) =>
        $"{DescribeCellAt(c.Row, c.Column)}\n" +
        $"{c.Lab}\nRGB {c.Rgb.ToHex()}  spread {c.Spread:0.0}\n" +
        $"ΔE00 from bare metal {c.DeltaEFromBare:0.0}\n" +
        $"Result: {Label(o)}" + (o == c.Suggested && c.Why.Length > 0 ? $" ({c.Why})" : "") +
        "\n(click to change)";

    // ---------------------------------------------------------------- save

    private void Save()
    {
        if (_fit is null || _measure is null || _outcomes is null || _capture is null) return;
        if (_ctx.Existing is { } existing)
        {
            var oc = _outcomes;
            var notes = $"{(_capture.Kind == CaptureKind.FlatbedScan ? "Scan" : "Photo")} {_capture.FilePath ?? _capture.Name} ({_capture.Source}); " +
                        $"grid {(_placedFromJob ? "placed at the engraving position" : _handPlaced || _manual || _detection is null ? "placed by hand" : "found automatically")}, fit {_fit.RmsPixels:0.0} px from {_fit.CellsUsed} squares";
            var results = GridReadout.ToSamples(existing, _measure, (r, c) => oc[r, c], _capture, _turn, _fit.Grid.IsPerspective, notes);
            Close(new ScanImportResult(existing, results, null, ForExistingGrid: true));
            return;
        }
        var d = Definition();
        if (d.Problem() is not null) return;
        var planned = SelectedPlanned;
        var card = new CardInfo
        {
            CardId = CardIdBox.Text?.Trim() ?? "",
            Lot = LotBox.Text?.Trim() ?? "",
            Cleaning = CleaningBox.Text?.Trim() ?? "",
            MakeItVersion = VersionBox.Text?.Trim() ?? "",
        };
        var kind = d.IsSwept(CardAxis.Frequency) && d.IsSwept(CardAxis.Power) ? "colour test" : "test array";
        var title = $"{(card.CardId.Length > 0 ? card.CardId + " — " : "")}{kind} τ{d.PulseWidthNs:0.#}";
        bool scanned = _capture.Kind == CaptureKind.FlatbedScan && !_fit.Grid.IsPerspective;
        var grid = d.ToGrid(title, _material.Id, _source, _lens,
                            cellPitchMm: scanned && _img?.Dpi is { } dpi ? _fit.Grid.Pitch / dpi * 25.4 : 3.5,
                            card: card, purpose: planned?.Purpose is { Length: > 0 } p ? p : "MakeIt " + kind);

        var now = DateTimeOffset.UtcNow;
        var samples = new List<Sample>(grid.Cells.Count);
        foreach (var cell in grid.Cells)
        {
            var r = _measure.At(cell.Row, cell.Column);
            samples.Add(new Sample
            {
                MaterialId = _material.Id, SourceId = _source.Id, LensId = _lens.Id,
                GridId = grid.Id, CellLabel = cell.Label, Settings = cell.Settings,
                Outcome = _outcomes[cell.Row, cell.Column],
                Color = r.Lab,
                Method = _capture.Method,
                ViewingCondition = _capture.ViewingCondition(_turn, _fit.Grid.IsPerspective),
                MeasuredUtc = now,
                Notes = $"{(_capture.Kind == CaptureKind.FlatbedScan ? "Scan" : "Photo")} {_capture.FilePath ?? _capture.Name} ({_capture.Source}); " +
                        $"grid {(_placedFromJob ? "placed at the engraving position" : _handPlaced || _manual || _detection is null ? "placed by hand" : "found automatically")}, fit {_fit.RmsPixels:0.0} px from {_fit.CellsUsed} squares; " +
                        $"spread {r.Spread:0.0}; ΔE00 from bare {r.DeltaEFromBare:0.0}",
            });
        }
        Close(new ScanImportResult(grid, samples, planned?.Id));
    }
}
