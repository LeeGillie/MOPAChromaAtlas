using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LumosUltra.Client.Models;
using SixLabors.ImageSharp; // for the SaveAsPng/Mutate extension methods below
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace LumosUltra.Client.TestHarness.Views;

/// <summary>
/// Manual, segmented test harness for LumosUltra.Client: one tab per feature area, so a
/// single call can be exercised against real hardware in isolation, with its exact
/// request/response visible. Not a substitute for the library's own automated tests -
/// this is for hands-on bring-up against a physical machine.
/// </summary>
public partial class MainWindow : Window
{
    private LumosUltraClient? _client;
    private byte[]? _lastPhoto;
    private DispatcherTimer? _statusTimer;
    private readonly HarnessSettings _settings = HarnessSettings.Load();

    private bool _panning;
    private Avalonia.Point _panPointerStart;
    private Avalonia.Point _panStart;

    // Camera view state, applied as one explicitly-built Matrix rather than a
    // TransformGroup of separate Scale/Translate children - a first attempt using two
    // child transforms combined in an unexpected order, causing zoom to visibly drift the
    // pan instead of anchoring to the cursor. Building the matrix directly (screenPoint =
    // zoom * localPoint + pan) removes that ambiguity entirely.
    private double _zoom = 1;
    private double _panX, _panY;

    public MainWindow()
    {
        InitializeComponent();

        // Zoom/pan only - the confirmed 90-degree rotation is baked into the bitmap's
        // actual pixels in TakePhotoAsync instead of applied as a RenderTransform here.
        // A control-level rotate transform rotates the already-laid-out (landscape) box
        // post-layout, which badly misaligns against a differently-shaped viewport;
        // rotating the pixels once means the Image control just lays out correctly from
        // the start. CameraImage's RenderTransformOrigin is pinned to 0,0 in XAML - zoom's
        // anchor point is computed explicitly in OnCameraWheelChanged, and Avalonia's
        // default center-origin would otherwise add its own unaccounted pivot on top of it.
        ApplyCameraTransform();

        FramingFileBox.Text = _settings.FramingFilePath;
        FramingMd5Box.Text = _settings.FramingMd5;
        Closing += (_, _) => SaveFramingSettings();

        ConnectButton.Click += async (_, _) => await ConnectAsync();

        GetDeviceInfoButton.Click += async (_, _) => await RunSafeAsync(GetDeviceInfoButton, DeviceOutput,
            async () => Format(await _client!.GetDeviceInfoAsync()));
        HeartbeatButton.Click += async (_, _) => await RunSafeAsync(HeartbeatButton, DeviceOutput,
            async () => await _client!.IsReachableAsync() ? "Reachable." : "No response.");

        GetJobStatusButton.Click += async (_, _) => await RunSafeAsync(GetJobStatusButton, JobStatusOutput, GetJobStatusTextAsync);
        AutoRefreshStatusBox.IsCheckedChanged += (_, _) => ToggleAutoRefresh();

        StartFramingButton.Click += async (_, _) => await RunSafeAsync(StartFramingButton, FramingOutput, async () =>
        {
            SaveFramingSettings();
            await _client!.StartFramingPreviewAsync(FramingFileBox.Text ?? "", FramingMd5Box.Text ?? "");
            return "Preview started.";
        });
        StopFramingButton.Click += async (_, _) => await RunSafeAsync(StopFramingButton, FramingOutput, async () =>
        {
            await _client!.StopFramingPreviewAsync();
            return "Preview stopped.";
        });

        TakePhotoButton.Click += async (_, _) => await TakePhotoAsync();
        SavePhotoButton.Click += async (_, _) => await SavePhotoAsync();
        ResetCameraViewButton.Click += (_, _) => ResetCameraView();
        CameraViewport.PointerWheelChanged += OnCameraWheelChanged;
        CameraViewport.PointerPressed += OnCameraPointerPressed;
        CameraViewport.PointerMoved += OnCameraPointerMoved;
        CameraViewport.PointerReleased += OnCameraPointerReleased;

        CheckDoorButton.Click += async (_, _) => await CheckDoorAsync();

        LoadGCodeFileButton.Click += async (_, _) => await LoadGCodeFileAsync();
        UploadArmButton.Click += async (_, _) => await RunSafeAsync(UploadArmButton, JobUploadOutput, async () =>
        {
            await _client!.UploadAndArmJobAsync(
                GCodeBox.Text ?? "",
                ftpUser: string.IsNullOrEmpty(FtpUserBox.Text) ? "mbtc" : FtpUserBox.Text,
                ftpPassword: string.IsNullOrEmpty(FtpPasswordBox.Text) ? "mbtc" : FtpPasswordBox.Text,
                requireDoorConfirmedClosed: RequireDoorClosedBox.IsChecked == true);
            return "Uploaded and armed. Not started.";
        });
        ConfirmSafeToFireBox.IsCheckedChanged += (_, _) => StartJobButton.IsEnabled = ConfirmSafeToFireBox.IsChecked == true;
        StartJobButton.Click += async (_, _) => await RunSafeAsync(StartJobButton, JobUploadOutput, async () =>
        {
            await _client!.StartArmedJobAsync();
            return "Start sent.";
        });

        RawSendButton.Click += async (_, _) => await RunSafeAsync(RawSendButton, RawOutput, async () =>
        {
            var method = (RawMethodBox.SelectedItem as ComboBoxItem)?.Content as string ?? "GET";
            // The raw-command endpoint runs M/G-code immediately with no lid interlock: not from this window.
            if ((RawPathBox.Text ?? "").Contains("/test/cmd", StringComparison.OrdinalIgnoreCase))
                return "Refused: /test/cmd/* runs commands immediately, without the lid interlock. Not available from this window.";
            return method == "POST"
                ? await _client!.PostRawJsonAsync(RawPathBox.Text ?? "", RawBodyBox.Text ?? "{}")
                : await _client!.GetRawAsync(RawPathBox.Text ?? "");
        });

        Closing += (_, _) => { _statusTimer?.Stop(); _client?.Dispose(); SaveFramingSettings(); };
    }

    private void SaveFramingSettings()
    {
        _settings.FramingFilePath = FramingFileBox.Text;
        _settings.FramingMd5 = FramingMd5Box.Text;
        _settings.Save();
    }

    // ---------------------------------------------------------------- connection

    private async Task ConnectAsync()
    {
        _client?.Dispose();
        _client = null;
        ConnectionStatus.Classes.Clear();
        ConnectionStatus.Text = "Connecting...";
        try
        {
            var address = BaseAddressBox.Text ?? LumosUltraClient.DefaultUsbBaseAddress;
            var client = new LumosUltraClient(address);
            bool ok = await client.IsReachableAsync();
            _client = client;
            ConnectionStatus.Text = ok ? $"Connected to {address}" : $"Created client for {address}, but heartbeat did not respond.";
            ConnectionStatus.Classes.Add(ok ? "ok" : "err");
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = FormatError(ex);
            ConnectionStatus.Classes.Add("err");
        }
    }

    private void EnsureConnected()
    {
        if (_client is null)
            throw new InvalidOperationException("Not connected. Enter a base address and click Connect first.");
    }

    private async Task RunSafeAsync(Button trigger, TextBox output, Func<Task<string>> action)
    {
        trigger.IsEnabled = false;
        try
        {
            EnsureConnected();
            output.Text = await action();
        }
        catch (Exception ex)
        {
            output.Text = FormatError(ex);
        }
        finally
        {
            trigger.IsEnabled = true;
        }
    }

    /// <summary>HttpRequestException's own Message is often a generic "An error occurred
    /// while sending the request." - the actual cause (e.g. a SocketException) is in
    /// InnerException, sometimes nested more than one level deep. Show the whole chain.</summary>
    private static string FormatError(Exception ex)
    {
        var sb = new StringBuilder($"ERROR: {ex.GetType().Name}: {ex.Message}");
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            sb.Append($"\n  caused by {inner.GetType().Name}: {inner.Message}");
        return sb.ToString();
    }

    // ---------------------------------------------------------------- job status

    private async Task<string> GetJobStatusTextAsync()
    {
        var status = await _client!.GetJobStatusAsync();
        return $"{status} ({(int)status})  ·  {DateTime.Now:HH:mm:ss}";
    }

    private void ToggleAutoRefresh()
    {
        if (AutoRefreshStatusBox.IsChecked == true)
        {
            _statusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _statusTimer.Tick -= OnStatusTick;
            _statusTimer.Tick += OnStatusTick;
            _statusTimer.Start();
        }
        else
        {
            _statusTimer?.Stop();
        }
    }

    private async void OnStatusTick(object? sender, EventArgs e)
    {
        if (_client is null) return;
        try { JobStatusOutput.Text = await GetJobStatusTextAsync(); }
        catch (Exception ex) { JobStatusOutput.Text = FormatError(ex); }
    }

    // ---------------------------------------------------------------- camera

    private async Task TakePhotoAsync()
    {
        TakePhotoButton.IsEnabled = false;
        try
        {
            EnsureConnected();
            CameraStatus.Text = "Capturing...";
            var bytes = await _client!.TakePhotoAsync();
            _lastPhoto = bytes; // Saved files stay the raw, unrotated bytes the device returned.
            CameraImage.Source = RotateForDisplay(bytes);
            ResetCameraView();
            SavePhotoButton.IsEnabled = true;
            CameraStatus.Text = $"{bytes.Length:N0} bytes  ·  {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            CameraStatus.Text = FormatError(ex);
        }
        finally
        {
            TakePhotoButton.IsEnabled = true;
        }
    }

    private async Task SavePhotoAsync()
    {
        if (_lastPhoto is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save photo",
            SuggestedFileName = $"lumos-ultra-{DateTime.Now:yyyyMMdd-HHmmss}.jpg",
            FileTypeChoices = [new FilePickerFileType("JPEG") { Patterns = ["*.jpg"] }],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(_lastPhoto);
    }

    private const double MinCameraZoom = 0.2, MaxCameraZoom = 10;

    /// <summary>The one place the view state (<see cref="_zoom"/>, <see cref="_panX"/>,
    /// <see cref="_panY"/>) becomes an actual transform: screenPoint = zoom * localPoint +
    /// (panX, panY), built as a raw Matrix so there is no ambiguity from combining
    /// separate child transforms in an order that turned out not to match expectations.</summary>
    private void ApplyCameraTransform() =>
        CameraImage.RenderTransform = new MatrixTransform(new Matrix(_zoom, 0, 0, _zoom, _panX, _panY));

    private void ResetCameraView()
    {
        _zoom = 1;
        _panX = 0;
        _panY = 0;
        ApplyCameraTransform();
    }

    private void OnCameraWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double oldZoom = _zoom;
        double factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        double newZoom = Math.Clamp(oldZoom * factor, MinCameraZoom, MaxCameraZoom);
        if (newZoom != oldZoom)
        {
            // Keep whatever image point is currently under the cursor fixed on screen,
            // rather than always zooming toward the image's own center. Stretch="Uniform"
            // letterboxes CameraImage's own layout Bounds to the content's aspect ratio
            // instead of filling CameraViewport (confirmed via on-screen diagnostics: Bounds
            // sat at a constant, non-zero offset inside the viewport at every zoom level) -
            // the render transform's local space starts at that Bounds offset, not at the
            // viewport's origin, so it has to be subtracted out and back in here.
            var cursor = e.GetPosition(CameraViewport);
            var origin = CameraImage.Bounds.Position;
            double localX = (cursor.X - origin.X - _panX) / oldZoom;
            double localY = (cursor.Y - origin.Y - _panY) / oldZoom;
            _panX = cursor.X - origin.X - newZoom * localX;
            _panY = cursor.Y - origin.Y - newZoom * localY;
            _zoom = newZoom;
            ApplyCameraTransform();
        }
        e.Handled = true;
    }

    /// <summary>Bakes the confirmed 90-degree clockwise rotation into real pixels (see
    /// <see cref="LumosUltra.Client.LumosUltraClient.TakePhotoAsync"/>), so the Image
    /// control lays out correctly from a normal portrait bitmap instead of a rotated
    /// RenderTransform fighting its own landscape-based layout.</summary>
    private static Bitmap RotateForDisplay(byte[] jpegBytes)
    {
        using var image = ImageSharpImage.Load(jpegBytes);
        image.Mutate(x => x.Rotate(RotateMode.Rotate90));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }

    private void OnCameraPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CameraViewport).Properties.IsLeftButtonPressed) return;
        _panning = true;
        _panPointerStart = e.GetPosition(CameraViewport);
        _panStart = new Avalonia.Point(_panX, _panY);
        e.Pointer.Capture(CameraViewport);
    }

    private void OnCameraPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning) return;
        var pos = e.GetPosition(CameraViewport);
        _panX = _panStart.X + (pos.X - _panPointerStart.X);
        _panY = _panStart.Y + (pos.Y - _panPointerStart.Y);
        ApplyCameraTransform();
    }

    private void OnCameraPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _panning = false;
        e.Pointer.Capture(null);
    }

    // ---------------------------------------------------------------- door

    private async Task CheckDoorAsync()
    {
        CheckDoorButton.IsEnabled = false;
        try
        {
            EnsureConnected();
            var closed = await _client!.TryIsDoorClosedAsync();
            DoorStatusResult.Text = closed switch { true => "Closed", false => "NOT closed (or unrecognised value)", null => "Unknown" };
            DoorStatusResult.Classes.Clear();
            DoorStatusResult.Classes.Add(closed == true ? "ok" : closed == false ? "err" : "dim");
            DoorOutput.Text = $"TryIsDoorClosedAsync() = {(closed is null ? "null" : closed.ToString())}  ·  {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            DoorOutput.Text = FormatError(ex);
        }
        finally
        {
            CheckDoorButton.IsEnabled = true;
        }
    }

    // ---------------------------------------------------------------- job upload

    private async Task LoadGCodeFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load G-code file",
            AllowMultiple = false,
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        GCodeBox.Text = await reader.ReadToEndAsync();
    }

    // ---------------------------------------------------------------- formatting

    private static string Format(DeviceInfo? info)
    {
        if (info is null) return "(no data)";
        var sb = new StringBuilder();
        void Line(string label, string? value) { if (!string.IsNullOrEmpty(value)) sb.AppendLine($"{label,-20} {value}"); }
        Line("App version", info.AppVersion);
        Line("Device type", info.DeviceType);
        Line("Hostname", info.Hostname);
        Line("Laser ID", info.LaserId);
        Line("Name", info.Name);
        Line("Serial number", info.SerialNumber);
        Line("SoC build time", info.SocImageBuildTime);
        Line("MCU version 1", info.McuVersion1);
        Line("MCU version 2", info.McuVersion2);
        Line("WLAN0 IP", info.Wlan0Ip);
        Line("WLAN0 MAC", info.Wlan0Mac);
        Line("Connect type", info.ConnectType);
        Line("Connect IP", info.ConnectIp);
        Line("MAC", info.Mac);
        Line("Power", info.Power);
        if (info.Extra.Count > 0)
        {
            sb.AppendLine().AppendLine("Unrecognised fields (JsonExtensionData):");
            foreach (var (key, value) in info.Extra)
                sb.AppendLine($"  {key,-18} {JsonSerializer.Serialize(value)}");
        }
        return sb.ToString();
    }
}
