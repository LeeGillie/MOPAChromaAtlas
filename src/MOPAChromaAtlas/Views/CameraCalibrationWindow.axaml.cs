using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using LumosUltra.Client;
using LumosUltra.Client.Models;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Scanning;
using MOPAChromaAtlas.Capture;

namespace MOPAChromaAtlas.Views;

/// <summary>
/// Calibrates the laser's camera to the bed (<see cref="BedCamera"/>): engrave the nine-cross
/// target (<see cref="BedCameraTarget"/>), photograph it with the laser's camera, click each
/// cross in turn. Saved in the capture settings; returns true when saved.
/// </summary>
public partial class CameraCalibrationWindow : Window
{
    private readonly CaptureSettings _settings = CaptureSettings.Load();
    private readonly CancellationTokenSource _cts = new();
    // Clicked cross centres, in any order; Match works out which cross each one is.
    private readonly List<PointD> _clicks = [];
    private BedCameraTarget.Matching? _match;
    private BedCamera? _fit => _match?.Camera;
    private byte[]? _lastJpeg;

    public CameraCalibrationWindow()
    {
        InitializeComponent();
        Photo.EmptyText = "Step 1: engrave the target (left). Its picture appears here afterwards, ready for step 2.";
        Photo.Clicked = OnPhotoClicked;
        Photo.CursorAt = _ => StandardCursorType.Cross;
        Photo.DrawOverlay = DrawOverlay;

        SendButton.Click += async (_, _) => await SendAsync();
        StopButton.Click += async (_, _) =>
        {
            try { using var c = new LumosUltraClient(_settings.LumosAddress); await c.CancelJobAsync(); EngraveText.Text = "Stop sent. If it keeps going, use the machine's stop button."; }
            catch (Exception ex) { EngraveText.Text = $"Could not send stop ({ex.Message}). Use the machine's stop button."; }
        };
        PhotoButton.Click += async (_, _) => await PhotoAsync();
        AlreadyButton.Click += async (_, _) => await PhotoAsync();
        BackButton.Click += (_, _) => ShowStep(1);
        TurnButton.Click += (_, _) => Reorient(turn: true);
        MirrorButton.Click += (_, _) => Reorient(turn: false);
        UndoButton.Click += (_, _) => UndoClick();
        ClearButton.Click += (_, _) => { _clicks.Clear(); Refit(); UpdateSteps(); Photo.Redraw(); };
        CancelButton.Click += (_, _) => Close(false);
        SaveButton.Click += (_, _) => SaveCalibration();
        KeyDown += (_, e) => { if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { UndoClick(); e.Handled = true; } };
        Closed += (_, _) => _cts.Cancel();
        ShowStep(1);
    }

    private MopaSettings TargetSettings() => new()
    {
        PowerPercent = (double)(PowerBox.Value ?? 40),
        SpeedMmPerS = (double)(SpeedBox.Value ?? 500),
        FrequencyKHz = (double)(FreqBox.Value ?? 40),
        PulseWidthNs = (double)(PwBox.Value ?? 200),
    };

    // ---------------------------------------------------------------- 1. engrave

    private async Task SendAsync()
    {
        SendButton.IsEnabled = false;
        EngraveText.Text = "Checking the laser…";
        try
        {
            using var client = new LumosUltraClient(_settings.LumosAddress);
            if (await client.TryIsDoorClosedAsync(_cts.Token) != true)
            {
                EngraveText.Text = "The door is not confirmed closed: close it and try again.";
                SendButton.IsEnabled = true;
                return;
            }
            await SafeSend.UploadAndArmAsync(client, GcodeExport.CalibrationTarget(TargetSettings()), null, _cts.Token);
            EngraveText.Text = "Armed. Press Start on the machine. Nothing fires until you do.";
            await FollowAsync(client);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EngraveText.Text = $"Could not send the target: {ex.Message}";
            SendButton.IsEnabled = true;
        }
    }

    /// <summary>Busy (2) once started; finished after two polls in a row that are not Busy. Then the picture.</summary>
    private async Task FollowAsync(LumosUltraClient client)
    {
        bool started = false;
        int notBusy = 0;
        var t0 = DateTime.Now;
        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(1000, _cts.Token);
            JobStatus st;
            try { st = await client.GetJobStatusAsync(_cts.Token); }
            catch (OperationCanceledException) { return; }
            catch { continue; }
            if (st == JobStatus.Busy)
            {
                if (!started) { started = true; t0 = DateTime.Now; StopButton.IsVisible = true; }
                notBusy = 0;
                EngraveText.Text = $"Engraving… {Durations.Clock(DateTime.Now - t0)}";
            }
            else if (started && ++notBusy >= 2)
            {
                StopButton.IsVisible = false;
                SendButton.IsEnabled = true;
                EngraveText.Text = "Engraved. Taking the picture…";
                await Task.Delay(2000, _cts.Token);
                await PhotoAsync();
                return;
            }
        }
    }

    // ---------------------------------------------------------------- 2. photograph

    private async Task PhotoAsync()
    {
        PhotoButton.IsEnabled = false;
        StatusText.Text = "Taking a picture with the laser's camera…";
        try
        {
            using var client = new LumosUltraClient(_settings.LumosAddress);
            _lastJpeg = await client.TakePhotoAsync(_cts.Token);
            await ShowPhotoAsync();
            ShowStep(2);
            StatusText.Text = "Picture taken.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not take a picture: {ex.Message}";
            EngraveText.Text = $"Could not take a picture with the laser's camera: {ex.Message}";
        }
        finally { PhotoButton.IsEnabled = true; }
    }

    private async Task ShowPhotoAsync()
    {
        if (_lastJpeg is null) return;
        var img = await Task.Run(() => _settings.LumosUpright(_lastJpeg));
        _clicks.Clear();
        _match = null;
        Photo.SetPicture(img);
        UpdateSteps();
    }

    private async void Reorient(bool turn)
    {
        if (turn) _settings.LumosQuarterTurns += _settings.LumosMirror ? -1 : 1;
        else _settings.LumosMirror = !_settings.LumosMirror;
        _settings.LumosQuarterTurns = ((_settings.LumosQuarterTurns % 4) + 4) % 4;
        _settings.Save();
        await ShowPhotoAsync();
    }

    // ---------------------------------------------------------------- 3. click

    private void OnPhotoClicked(PointD p)
    {
        if (!ClickPanel.IsVisible || Photo.Picture is null || _clicks.Count >= 12) return;
        _clicks.Add(p);
        Refit();
        UpdateSteps();
        Photo.Redraw();
    }

    private void UndoClick()
    {
        if (_clicks.Count == 0) return;
        _clicks.RemoveAt(_clicks.Count - 1);
        Refit();
        UpdateSteps();
        Photo.Redraw();
    }

    private void Refit() =>
        _match = _clicks.Count >= 5 && Photo.Picture is { } img
            ? BedCameraTarget.Match(_clicks, img.Width, img.Height, DateTimeOffset.UtcNow)
            : null;

    /// <summary>Matched well enough to trust: six or more crosses, and no other matching nearly as good.</summary>
    private bool Trustworthy => _match is { } m && m.Index.Count(i => i >= 0) >= 6 && m.Margin >= 2 && m.Camera.MaxMm <= 3;

    private void ShowStep(int step)
    {
        EngravePanel.IsVisible = step == 1;
        ClickPanel.IsVisible = step == 2;
        Photo.CursorAt = _ => step == 2 ? StandardCursorType.Cross : StandardCursorType.Arrow;
        UpdateSteps();
        Photo.Redraw();
    }

    private void UpdateSteps()
    {
        UndoButton.IsEnabled = ClearButton.IsEnabled = _clicks.Count > 0;
        int matched = _match?.Index.Count(i => i >= 0) ?? 0;
        int stray = _match?.Index.Count(i => i < 0) ?? 0;
        StepText.Text = _clicks.Count == 0 ? "Click the first + mark."
            : _clicks.Count < 5 ? $"{_clicks.Count} clicked. Keep going: Atlas starts recognising the marks after five."
            : _match is null ? $"{_clicks.Count} clicked, but they do not fit the target's layout. Check each click is on a + mark (zoom in), or clear and start again."
            : $"{_clicks.Count} clicked, {matched} of the 9 marks recognised.";

        if (_match is { } m)
        {
            var f = m.Camera;
            var lines = new List<string>();
            if (stray > 0) lines.Add($"A red ring with ? is a click that is not on a + mark. Undo it (or Clear all) and click the mark's centre.");
            if (m.Margin < 2) lines.Add("Not sure yet which mark is which: click more of them.");
            if (matched < 6) lines.Add($"Click at least {6 - matched} more mark{(6 - matched == 1 ? "" : "s")} (all nine is best).");
            if (f.MaxMm > 3) lines.Add("The clicks are not precise enough: zoom in and click the centres.");
            if (lines.Count == 0) lines.Add($"Good: the picture matches the bed to within {f.MaxMm:0.0} mm. Press Save calibration.");
            ResultText.Text = string.Join("\n", lines);
            ResultText.Foreground = lines[0].StartsWith("Good") ? new SolidColorBrush(Color.Parse("#5CC28A")) : new SolidColorBrush(Color.Parse("#F0C674"));
        }
        else ResultText.Text = "";
        SaveButton.IsEnabled = Trustworthy;
    }

    /// <summary>One ring per click: white while unrecognised, green with the mark's number once recognised, red with ? when it is not on a mark.</summary>
    private void DrawOverlay(Canvas canvas, Func<PointD, Point> toScreen, double scale)
    {
        for (int c = 0; c < _clicks.Count; c++)
        {
            var s = toScreen(_clicks[c]);
            int idx = _match is { } m && c < m.Index.Length ? m.Index[c] : -2;
            var brush = idx >= 0 ? Brushes.Lime : idx == -1 ? Brushes.OrangeRed : Brushes.White;
            AddRing(canvas, s, 10, brush, 2);
            AddLabel(canvas, s, idx >= 0 ? (idx + 1).ToString() : idx == -1 ? "?" : "", brush);
        }
    }

    private static void AddRing(Canvas c, Point p, double r, IBrush brush, double thick)
    {
        var e = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = brush, StrokeThickness = thick };
        Canvas.SetLeft(e, p.X - r);
        Canvas.SetTop(e, p.Y - r);
        c.Children.Add(e);
    }

    private static void AddLabel(Canvas c, Point p, string text, IBrush brush)
    {
        var t = new TextBlock { Text = text, Foreground = brush, FontSize = 13, FontWeight = FontWeight.Bold };
        Canvas.SetLeft(t, p.X + 10);
        Canvas.SetTop(t, p.Y - 22);
        c.Children.Add(t);
    }

    // ---------------------------------------------------------------- save

    public void SaveCalibration()
    {
        if (_fit is null || !Trustworthy) return;
        _settings.LumosCamera = _fit;
        _settings.LumosCameraOrientation = _settings.LumosOrientation;
        _settings.LumosCameraTargetVersion = BedCameraTarget.Version;
        _settings.Save();
        Close(true);
    }

    /// <summary>For tests and screenshots: use a picture as if it had come from the laser's camera.</summary>
    public void UsePicture(RgbImage img)
    {
        _clicks.Clear();
        _match = null;
        Photo.SetPicture(img);
        ShowStep(2);
    }

    /// <summary>For tests: click a picture point as the user would.</summary>
    public void ClickAt(PointD p) => OnPhotoClicked(p);
}
