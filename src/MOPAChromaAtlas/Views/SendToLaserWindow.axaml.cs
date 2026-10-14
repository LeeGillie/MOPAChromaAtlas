using Avalonia.Controls;
using Avalonia.Media;
using LumosUltra.Client;
using LumosUltra.Client.Models;
using MOPAChroma.Grids;
using MOPAChroma.Scanning;
using MOPAChromaAtlas.Capture;

namespace MOPAChromaAtlas.Views;

/// <summary>
/// Sends a planned Atlas grid to the Lumos Ultra and reads it back, in one place:
/// check the laser and door, build the job (with corner crosses) and arm it, wait for the
/// machine's Start button, follow the job to the end, photograph the card with the
/// laser's camera, and open the card reader on that photo. Returns the reader's result
/// (or null) so the main window can record it.
/// </summary>
public partial class SendToLaserWindow : Window
{
    private enum Phase { Ready, Armed, Running, Finished, Reading, Done, Failed }

    private readonly TestGrid _grid;
    private readonly ScanImportContext _readContext;
    private CaptureSettings _settings = CaptureSettings.Load();
    /// <summary>Take a camera picture as soon as the window opens (off for demos without a laser).</summary>
    public static bool AutoPhoto { get; set; } = true;
    // Where the middle of the job goes on the bed, mm. Chosen on the camera picture.
    private double _cx = 105, _cy = 105;
    private PointD _dragStartBed;
    private (double x, double y) _dragStartCentre;
    private bool _syncing;
    // True once the job has been drawn on a calibrated camera picture in this window: the
    // only way to know where it will land. Sending needs this, or an explicit override.
    private bool _placementSeen;
    private readonly CancellationTokenSource _cts = new();
    private Phase _phase = Phase.Ready;
    private DateTime _runStarted;
    /// <summary>Whether the picture has been zoomed onto the job once already (see PhotoAsync).</summary>
    private bool _zoomedToJob;

    private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#5CC28A"));
    private static readonly IBrush Busy = new SolidColorBrush(Color.Parse("#E3A640"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#EF6A70"));

    public SendToLaserWindow() : this(new TestGrid
    {
        Title = "Preview", MaterialId = "preview", SourceId = "", LensId = "",
        Layout = new GridLayout(), Cells = [],
    }, new ScanImportContext(new MOPAChroma.Atlas.Material { Id = "preview", Name = "Preview" },
        LumosUltra.Jobs.SourceProfile.Presets[0], LumosUltra.Jobs.LensProfile.Presets[0], [], null, "preview")) { }

    public SendToLaserWindow(TestGrid grid, ScanImportContext readContext)
    {
        InitializeComponent();
        _grid = grid;
        _readContext = readContext with { Existing = grid };
        Title = $"Send to the laser — {grid.Title}";
        GridTitle.Text = grid.Title;
        var l = grid.Layout;
        GridSummary.Text = $"{grid.Cells.Count} cells, {l.Columns} × {l.Rows}, {l.CellSizeMm:0.#} mm squares {l.GapMm:0.#} mm apart: " +
                           $"{l.WidthMm:0.#} × {l.HeightMm:0.#} mm. Powers {grid.Cells.Min(c => c.Settings.PowerPercent):0.#}-" +
                           $"{grid.Cells.Max(c => c.Settings.PowerPercent):0.#} %.";
        AddressBox.Text = _settings.LumosAddress;

        CheckButton.Click += async (_, _) => await CheckAsync();
        SendButton.Click += async (_, _) => await SendAsync();
        ConfirmBox.IsCheckedChanged += (_, _) => StartHereButton.IsEnabled = ConfirmBox.IsChecked == true && _phase == Phase.Armed;
        StartHereButton.Click += async (_, _) => await StartHereAsync();
        StopButton.Click += async (_, _) => await StopAsync();
        CloseButton.Click += (_, _) => Close(null);
        Closed += (_, _) => _cts.Cancel();

        // Placement on the camera picture.
        Photo.EmptyText = "The laser camera's picture appears here, with the job drawn where it will be engraved.";
        Photo.DrawOverlay = DrawJob;
        Photo.TryStartDrag = StartJobDrag;
        Photo.DragMoved = p => MoveJob(p);
        Photo.DragEnded = p => MoveJob(p);
        Photo.CursorAt = p => OverJob(p) ? Avalonia.Input.StandardCursorType.SizeAll : Avalonia.Input.StandardCursorType.Arrow;
        PhotoButton.Click += async (_, _) => await PhotoAsync();
        CalibrateButton.Click += async (_, _) => await CalibrateAsync();
        CalibrateNowButton.Click += async (_, _) => await CalibrateAsync();
        BlindBox.IsCheckedChanged += (_, _) => UpdateSendState();
        CentreButton.Click += (_, _) => SetCentre(105, 105);
        CentreXBox.ValueChanged += (_, _) => { if (!_syncing && CentreXBox.Value is { } v) SetCentre((double)v, _cy); };
        CentreYBox.ValueChanged += (_, _) => { if (!_syncing && CentreYBox.Value is { } v) SetCentre(_cx, (double)v); };
        MarksBox.IsCheckedChanged += (_, _) => SetCentre(_cx, _cy);
        SetCentre(105, 105);
        ShowCalibrationState();
        Opened += async (_, _) => { if (AutoPhoto && _settings.CurrentLumosCamera is not null) await PhotoAsync(); };
    }

    private LumosUltraClient Client()
    {
        var address = (AddressBox.Text ?? "").Trim();
        if (address.Length == 0) address = LumosUltraClient.DefaultUsbBaseAddress;
        if (address != _settings.LumosAddress) { _settings.LumosAddress = address; _settings.Save(); }
        return new LumosUltraClient(address);
    }

    private static void Show(TextBlock mark, TextBlock step, IBrush brush, string glyph, string text)
    {
        mark.Text = glyph;
        mark.Foreground = brush;
        step.Text = text;
    }

    // ---------------------------------------------------------------- 1. laser and door

    private async Task<bool> CheckAsync()
    {
        CheckButton.IsEnabled = false;
        Show(Mark1, Step1, Busy, "…", "Looking for the laser…");
        try
        {
            using var client = Client();
            if (!await client.IsReachableAsync(_cts.Token))
            {
                Show(Mark1, Step1, Bad, "✕", $"The laser did not answer at {AddressBox.Text}. Is it switched on and connected?");
                return false;
            }
            var info = await client.GetDeviceInfoAsync(_cts.Token);
            var door = await client.TryIsDoorClosedAsync(_cts.Token);
            string who = info is null ? "Laser found" : $"{info.Name ?? "Laser"} ({info.DeviceType}, {info.Power}, firmware {info.McuVersion1 ?? info.AppVersion})";
            if (door != true)
            {
                Show(Mark1, Step1, Bad, "✕", $"{who}. The door is {(door == false ? "open" : "not confirmed closed")}: close it and check again.");
                return false;
            }
            Show(Mark1, Step1, Ok, "✓", $"{who}. Door closed.");
            return true;
        }
        catch (Exception ex)
        {
            Show(Mark1, Step1, Bad, "✕", $"Could not reach the laser: {ex.Message}");
            return false;
        }
        finally { CheckButton.IsEnabled = _phase == Phase.Ready; }
    }

    // ---------------------------------------------------------------- 2. build, send, arm

    private async Task SendAsync()
    {
        if (_phase != Phase.Ready) return;
        if (!_placementSeen && BlindBox.IsChecked != true) { UpdateSendState(); return; }
        SendButton.IsEnabled = false;
        if (!await CheckAsync()) { SendButton.IsEnabled = true; return; }

        // Last line of defence: nothing above the absolute heat limit ever leaves this window.
        var tooHot = MOPAChroma.Planning.HeatLimits.TooHot(_grid, MOPAChroma.Planning.HeatLimits.AbsoluteMaxJPerMm2, _readContext.Source, _readContext.Lens);
        if (tooHot.Count > 0)
        {
            Show(Mark2, Step2, Bad, "✕", $"Not sent: {tooHot.Count} cell(s) above the safe heat limit ({MOPAChroma.Planning.HeatLimits.AbsoluteMaxJPerMm2:0} J/mm²), " +
                 $"hottest {tooHot[0].Label} at {tooHot[0].JPerMm2:0} J/mm². Plan a new grid.");
            return;
        }
        string gcode = GcodeExport.ToGcode(_grid, _cx, _cy, cornerMarks: MarksBox.IsChecked == true);
        string? kept = KeepJob(gcode);
        Show(Mark2, Step2, Busy, "…", $"Sending the job ({gcode.Length / 1024.0:0} KB)…");
        try
        {
            using var client = Client();
            // The library re-checks the door itself and refuses to arm unless it is confirmed closed.
            await SafeSend.UploadAndArmAsync(client, gcode, _readContext.Source, _cts.Token);
        }
        catch (UnsafeJobException ex)
        {
            Show(Mark2, Step2, Bad, "✕", ex.Message);
            return; // never re-enabled: this job must not be sent
        }
        catch (Exception ex)
        {
            Show(Mark2, Step2, Bad, "✕", $"Could not send the job: {ex.Message}");
            SendButton.IsEnabled = true;
            return;
        }

        _phase = Phase.Armed;
        MarksBox.IsEnabled = false;
        CentreXBox.IsEnabled = CentreYBox.IsEnabled = CentreButton.IsEnabled = false;
        NotPlacedPanel.IsVisible = false;
        Photo.Redraw();
        CheckButton.IsEnabled = false;
        Show(Mark2, Step2, Ok, "✓", $"Job sent and armed, centred at X {_cx:0.0}, Y {_cy:0.0} mm." + (kept is null ? "" : $" A copy is kept as {Path.GetFileName(kept)}."));
        Show(Mark3, Step3, Busy, "▶", "Press Start on the machine. Nothing fires until you do.");
        StartHerePanel.IsVisible = true;
        StatusText.Text = "Armed and waiting for the Start button.";
        _ = FollowAsync();
    }

    // ---------------------------------------------------------------- where it goes

    private (double MinX, double MinY, double MaxX, double MaxY) Extents(double cx, double cy) =>
        GcodeExport.Extents(_grid, cx, cy, MarksBox.IsChecked == true);

    /// <summary>Moves the job, keeping all of it (crosses included) at least 2 mm inside the 210 mm bed.</summary>
    private void SetCentre(double cx, double cy)
    {
        if (_phase != Phase.Ready) return;
        var (x0, y0, x1, y1) = Extents(105, 105);
        double hx0 = 105 - x0, hx1 = x1 - 105, hy0 = 105 - y0, hy1 = y1 - 105;
        _cx = Math.Clamp(cx, 2 + hx0, 208 - hx1);
        _cy = Math.Clamp(cy, 2 + hy0, 208 - hy1);
        _syncing = true;
        CentreXBox.Value = (decimal)Math.Round(_cx, 1);
        CentreYBox.Value = (decimal)Math.Round(_cy, 1);
        _syncing = false;
        Photo.Redraw();
    }

    private bool OverJob(PointD p)
    {
        if (_settings.CurrentLumosCamera is not { } cam || _phase != Phase.Ready) return false;
        var b = cam.ToBed(p);
        var (x0, y0, x1, y1) = Extents(_cx, _cy);
        return b.X >= x0 - 2 && b.X <= x1 + 2 && b.Y >= y0 - 2 && b.Y <= y1 + 2;
    }

    private bool StartJobDrag(PointD p)
    {
        if (!OverJob(p)) return false;
        _dragStartBed = _settings.CurrentLumosCamera!.ToBed(p);
        _dragStartCentre = (_cx, _cy);
        return true;
    }

    private void MoveJob(PointD p)
    {
        if (_settings.CurrentLumosCamera is not { } cam) return;
        var d = cam.ToBed(p) - _dragStartBed;
        SetCentre(_dragStartCentre.x + d.X, _dragStartCentre.y + d.Y);
    }

    /// <summary>The job drawn on the camera picture: its outline, every cell, and the corner crosses.</summary>
    private void DrawJob(Canvas canvas, Func<PointD, Avalonia.Point> toScreen, double scale)
    {
        if (_settings.CurrentLumosCamera is not { } cam) return;
        var colour = _phase == Phase.Ready ? Color.Parse("#4FD1FF") : Color.Parse("#E3A640");
        var l = _grid.Layout;
        double ox = _cx - l.WidthMm / 2, oy = _cy - l.HeightMm / 2, pitch = l.CellSizeMm + l.GapMm;

        // Straight lines on the bed bend a little in the picture, so edges are drawn through several points.
        IEnumerable<Avalonia.Point> Edge(double x0, double y0, double x1, double y1, int n) =>
            Enumerable.Range(0, n + 1).Select(i => toScreen(cam.ToImage(new PointD(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n))));
        Avalonia.Controls.Shapes.Polygon Box(double x0, double y0, double x1, double y1, int n, IBrush? fill, double thick) => new()
        {
            Points = Edge(x0, y0, x1, y0, n).Concat(Edge(x1, y0, x1, y1, n)).Concat(Edge(x1, y1, x0, y1, n)).Concat(Edge(x0, y1, x0, y0, n)).ToList(),
            Stroke = new SolidColorBrush(colour), StrokeThickness = thick, Fill = fill,
        };

        var (ex0, ey0, ex1, ey1) = Extents(_cx, _cy);
        var outline = Box(ex0 - 1, ey0 - 1, ex1 + 1, ey1 + 1, 12, new SolidColorBrush(Color.FromArgb(25, colour.R, colour.G, colour.B)), 2);
        outline.StrokeDashArray = [5, 3];
        canvas.Children.Add(outline);
        var cellFill = new SolidColorBrush(Color.FromArgb(70, colour.R, colour.G, colour.B));
        for (int r = 0; r < l.Rows; r++)
            for (int c = 0; c < l.Columns; c++)
            {
                double x = ox + c * pitch, y = oy + r * pitch;
                canvas.Children.Add(Box(x, y, x + l.CellSizeMm, y + l.CellSizeMm, 1, cellFill, 1));
            }
        if (MarksBox.IsChecked == true)
        {
            double half = CornerMarks.SizeMm(l) / 2, off = CornerMarks.OffsetMm(l);
            (double, double)[] marks =
            [
                (ox + l.CellSizeMm / 2, oy + l.CellSizeMm / 2 - off),
                (ox + (l.Columns - 1) * pitch + l.CellSizeMm / 2, oy + l.CellSizeMm / 2 - off),
                (ox + l.CellSizeMm / 2, oy + (l.Rows - 1) * pitch + l.CellSizeMm / 2 + off),
            ];
            foreach (var (mx, my) in marks)
            {
                canvas.Children.Add(new Avalonia.Controls.Shapes.Line { StartPoint = toScreen(cam.ToImage(new PointD(mx - half, my))), EndPoint = toScreen(cam.ToImage(new PointD(mx + half, my))), Stroke = new SolidColorBrush(colour), StrokeThickness = 1.5 });
                canvas.Children.Add(new Avalonia.Controls.Shapes.Line { StartPoint = toScreen(cam.ToImage(new PointD(mx, my - half))), EndPoint = toScreen(cam.ToImage(new PointD(mx, my + half))), Stroke = new SolidColorBrush(colour), StrokeThickness = 1.5 });
            }
            // The grid number's dots, between the top crosses: drawn at least 2 px so they show at any zoom.
            foreach (var (dx, dy, d) in CardId.DotsMm(l, _grid.Number, ox, oy))
            {
                var a = toScreen(cam.ToImage(new PointD(dx - d / 2, dy - d / 2)));
                var b = toScreen(cam.ToImage(new PointD(dx + d / 2, dy + d / 2)));
                double w = Math.Max(2, Math.Abs(b.X - a.X)), h = Math.Max(2, Math.Abs(b.Y - a.Y));
                var dot = new Avalonia.Controls.Shapes.Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(colour) };
                Canvas.SetLeft(dot, (a.X + b.X) / 2 - w / 2);
                Canvas.SetTop(dot, (a.Y + b.Y) / 2 - h / 2);
                canvas.Children.Add(dot);
            }
            // "Grid 29", as it will be engraved.
            foreach (var line in GridLabel.StrokesMm(l, _grid.Number, ox, oy))
                canvas.Children.Add(new Avalonia.Controls.Shapes.Polyline
                {
                    Points = line.Select(p => toScreen(cam.ToImage(new PointD(p.X, p.Y)))).ToList(),
                    Stroke = new SolidColorBrush(colour), StrokeThickness = 1.2,
                });
        }
        var label = new TextBlock
        {
            Text = _phase == Phase.Ready ? "will engrave here · drag to move" : "will engrave here",
            Foreground = new SolidColorBrush(colour), FontSize = 12, FontWeight = FontWeight.SemiBold,
        };
        var at = toScreen(cam.ToImage(new PointD(ex0 - 1, ey1 + 1)));
        Canvas.SetLeft(label, at.X);
        Canvas.SetTop(label, at.Y + 4);
        canvas.Children.Add(label);
    }

    private void ShowCalibrationState()
    {
        var cam = _settings.CurrentLumosCamera;
        PreviewText.Text = cam is not null
            ? $"Camera calibrated {cam.Calibrated.ToLocalTime():d MMM yyyy} (within {cam.MaxMm:0.0} mm). Only true at the height it was calibrated at."
            : _settings.LumosCamera is not null
                ? "The camera calibration is out of date (made with the old target, or with the picture turned another way): calibrate again."
                : "The camera is not calibrated yet.";
        PlaceText.Text = "Put the blank on the bed and focus as usual. Take a picture: the job is drawn on it where it will engrave. Drag it onto the blank, take a fresh picture to check, then send.";
        UpdateSendState();
    }

    /// <summary>
    /// Sending is only offered once the job has been seen on a calibrated camera picture, or
    /// with the explicit "send without seeing it" tick: a bed position on its own says nothing
    /// about where the blank is.
    /// </summary>
    private void UpdateSendState()
    {
        bool calibrated = _settings.CurrentLumosCamera is not null;
        NotPlacedPanel.IsVisible = _phase == Phase.Ready && !_placementSeen;
        CalibrateNowButton.IsVisible = !calibrated;
        NotPlacedText.Text = calibrated
            ? "Take a picture (top right) to see where the job will engrave before sending it."
            : "Atlas cannot know where the job lands on your bed until the laser's camera is calibrated. " +
              "Calibrate once: it engraves nine small crosses, so cover most of the bed with a big scrap (a sheet of cardboard is fine). " +
              "After that every job is shown on the camera picture, and you drag it onto the blank.";
        if (_phase == Phase.Ready)
        {
            SendButton.IsEnabled = _placementSeen || BlindBox.IsChecked == true;
            ToolTip.SetTip(SendButton, SendButton.IsEnabled
                ? "Build the job, check the door is closed, send it to the laser and arm it. Nothing fires until Start is pressed."
                : "First see where it will engrave: take a picture (calibrate the camera first if asked).");
            if (!SendButton.IsEnabled) StatusText.Text = "Not sent: first check on the camera picture where it will engrave.";
            else if (StatusText.Text?.StartsWith("Not sent:") == true) StatusText.Text = "";
        }
    }

    private async Task PhotoAsync()
    {
        PhotoButton.IsEnabled = false;
        PreviewText.Text = "Taking a picture with the laser's camera…";
        try
        {
            _settings = CaptureSettings.Load();
            using var client = Client();
            var bytes = await client.TakePhotoAsync(_cts.Token);
            var img = await Task.Run(() => _settings.LumosUpright(bytes));
            Photo.SetPicture(img, keepView: true);
            _placementSeen = _settings.CurrentLumosCamera is not null;
            ShowCalibrationState();
            // Zoom to the job the first time only, so it can be placed precisely. Every later
            // picture keeps the zoom and position the user has (SetPicture keepView), so taking
            // pictures while it engraves does not keep jumping back.
            if (!_zoomedToJob && _settings.CurrentLumosCamera is { } cam)
            {
                _zoomedToJob = true;
                var (x0, y0, x1, y1) = Extents(_cx, _cy);
                var pts = new[] { new PointD(x0, y0), new PointD(x1, y0), new PointD(x0, y1), new PointD(x1, y1) }.Select(cam.ToImage).ToArray();
                double w = Math.Max(pts.Max(p => p.X) - pts.Min(p => p.X), img.Width * 0.25), h = Math.Max(pts.Max(p => p.Y) - pts.Min(p => p.Y), img.Height * 0.25);
                double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y);
                Photo.ZoomTo(mx - w, my - h, mx + w, my + h);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { PreviewText.Text = $"Could not take a picture: {ex.Message}"; }
        finally { PhotoButton.IsEnabled = true; }
    }

    private async Task CalibrateAsync()
    {
        var saved = await CameraCalibrationWindow.ShowAsync(this, _settings.Lumos);
        _settings = CaptureSettings.Load();
        ShowCalibrationState();
        if (saved) await PhotoAsync();
    }

    /// <summary>For demos and checks: put the job's centre at a bed position.</summary>
    public void SetJobCentre(double cx, double cy) => SetCentre(cx, cy);

    /// <summary>For screenshots: show a picture as if from the laser's camera.</summary>
    public void UsePicture(RgbImage img) { Photo.SetPicture(img); _placementSeen = _settings.CurrentLumosCamera is not null; ShowCalibrationState(); }

    /// <summary>Keeps the exact job sent beside the atlas, so a card can always be traced to its G-code.</summary>
    private string? KeepJob(string gcode)
    {
        try
        {
            var dir = Path.Combine(CaptureSettings.Folder, "jobs");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(_grid.Title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
            var path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss} {safe}.gc");
            File.WriteAllText(path, gcode);
            return path;
        }
        catch { return null; }
    }

    private async Task StartHereAsync()
    {
        if (_phase != Phase.Armed || ConfirmBox.IsChecked != true) return;
        StartHereButton.IsEnabled = false;
        try
        {
            using var client = Client();
            await client.StartArmedJobAsync(_cts.Token);
            StatusText.Text = "Start sent.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not start the job: {ex.Message}. The machine's Start button still works.";
            StartHereButton.IsEnabled = ConfirmBox.IsChecked == true;
        }
    }

    private async Task StopAsync()
    {
        try
        {
            using var client = Client();
            await client.CancelJobAsync(_cts.Token);
            StatusText.Text = "Stop sent. If the laser keeps going, use the stop button on the machine.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not send stop: {ex.Message}. Use the stop button on the machine."; }
    }

    // ---------------------------------------------------------------- 3-4. follow the job

    /// <summary>
    /// Polls the job status once a second. An armed job reports Idle (4) while it waits for
    /// the Start button; a running one Busy (2). The job counts as finished when Busy has
    /// been seen and then two polls in a row are not Busy.
    /// </summary>
    private async Task FollowAsync()
    {
        int notBusy = 0, errors = 0;
        using var client = Client();
        while (!_cts.IsCancellationRequested && _phase is Phase.Armed or Phase.Running)
        {
            try { await Task.Delay(1000, _cts.Token); } catch (OperationCanceledException) { return; }
            JobStatus status;
            try { status = await client.GetJobStatusAsync(_cts.Token); errors = 0; }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (++errors == 5) StatusText.Text = $"Lost touch with the laser ({ex.Message}). Still trying…";
                continue;
            }

            if (_phase == Phase.Armed && status == JobStatus.Busy)
            {
                _phase = Phase.Running;
                _runStarted = DateTime.Now;
                StartHerePanel.IsVisible = false;
                StopButton.IsVisible = true;
                Show(Mark3, Step3, Ok, "✓", $"Started at {_runStarted:HH:mm:ss}.");
            }
            if (_phase == Phase.Running)
            {
                var t = DateTime.Now - _runStarted;
                if (status == JobStatus.Busy)
                {
                    notBusy = 0;
                    Show(Mark4, Step4, Busy, "…", $"Engraving… {Durations.Clock(t)}");
                }
                else if (++notBusy >= 2)
                {
                    _phase = Phase.Finished;
                    StopButton.IsVisible = false;
                    Show(Mark4, Step4, Ok, "✓", $"Finished in {Durations.Clock(t)}.");
                    StatusText.Text = "Job finished.";
                    if (AutoReadBox.IsChecked == true) await ReadAsync();
                    else Show(Mark5, Step5, Busy, "–", "Read the card when you are ready: Read this grid from an image, in the main window.");
                }
            }
        }
    }

    // ---------------------------------------------------------------- 5. photograph and read

    private async Task ReadAsync()
    {
        _phase = Phase.Reading;
        Show(Mark5, Step5, Busy, "…", "Photographing the card with the laser's camera…");
        try
        {
            // A moment for the extraction to clear the smoke before the picture.
            await Task.Delay(3000, _cts.Token);
            using var client = Client();
            var bytes = await client.TakePhotoAsync(_cts.Token);
            var img = await Task.Run(() => _settings.LumosUpright(bytes));
            var capture = KeepPhoto(new CardCapture(img, CaptureKind.Photo, "Lumos Ultra camera", "lumos"));

            Show(Mark5, Step5, Busy, "…", "Finding the cells… check the result in the reader.");
            var reader = new ScanImportWindow(_readContext);
            var engravedAt = EngravedGridInPicture(capture.Image);
            reader.Opened += async (_, _) => await reader.UseCaptureAsync(capture, engravedAt);
            var result = await reader.ShowDialog<ScanImportResult?>(this);
            if (result is null)
            {
                _phase = Phase.Done;
                Show(Mark5, Step5, Busy, "–", "Reading cancelled. Read it later from the main window: the photo is kept in the captures folder.");
                return;
            }
            _phase = Phase.Done;
            Show(Mark5, Step5, Ok, "✓", $"Read {result.Samples.Count} cells.");
            Close(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _phase = Phase.Failed;
            Show(Mark5, Step5, Bad, "✕", $"Could not photograph the card: {ex.Message}. Read it from a scan or photo instead.");
        }
    }

    /// <summary>
    /// Where every square of the job just engraved appears in a laser-camera picture: its bed
    /// position (this window's job centre) through the camera calibration. Null without a
    /// calibration for pictures of this size and orientation.
    /// </summary>
    private GridGeometry? EngravedGridInPicture(RgbImage img)
    {
        var cam = CaptureSettings.Load().CurrentLumosCamera;
        return cam is not null && cam.ImageWidth == img.Width && cam.ImageHeight == img.Height ? cam.GridAt(_grid.Layout, _cx, _cy) : null;
    }

    private static CardCapture KeepPhoto(CardCapture c)
    {
        try
        {
            Directory.CreateDirectory(CaptureSettings.CapturesFolder);
            var path = Path.Combine(CaptureSettings.CapturesFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-lumos.png");
            c.Image.SavePng(path);
            return c with { Name = Path.GetFileName(path), FilePath = path };
        }
        catch { return c; }
    }
}
