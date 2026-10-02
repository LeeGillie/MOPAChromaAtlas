using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FlashCap;
using PixelFormats = FlashCap.PixelFormats;
using MOPAChroma.Scanning;
using MOPAChromaAtlas.Capture;

namespace MOPAChromaAtlas.Views;

/// <summary>What the camera window hands back: the averaged picture and which camera took it.</summary>
public sealed record CameraResult(RgbImage Image, string CameraName, string Format, int Frames);

/// <summary>
/// Live preview from a USB camera, and a capture that averages several frames. Uses
/// FlashCap, which talks to DirectShow / Media Foundation on Windows, V4L2 on Linux and
/// AVFoundation on macOS. Frames are decoded by Atlas itself (the same decoder the capture
/// uses), so a preview that works is a capture that works, and a frame that cannot be
/// decoded is reported instead of leaving the preview blank.
/// </summary>
public partial class CameraWindow : Window
{
    private readonly CaptureSettings _settings;
    private CaptureDeviceDescriptor[] _devices = [];
    private VideoCharacteristics[] _formats = [];
    private CaptureDevice? _device;
    private readonly object _gate = new();
    private DateTime _lastPreview = DateTime.MinValue;
    private List<byte[]>? _collecting;
    private int _wanted;
    private TaskCompletionSource<List<byte[]>>? _collected;
    private bool _switching;
    private int _frameCount, _shownCount;
    private string? _frameError;
    private DateTime _started;
    private readonly DispatcherTimer _health = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _describe = "";

    public CameraWindow() : this(new CaptureSettings()) { }

    public CameraWindow(CaptureSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        FramesBox.Value = Math.Clamp(settings.CameraFrames, 1, 32);

        RefreshButton.Click += (_, _) => _ = ListDevicesAsync();
        DeviceBox.SelectionChanged += (_, _) => { if (!_switching) FillFormats(); };
        FormatBox.SelectionChanged += (_, _) => { if (!_switching) _ = StartAsync(); };
        SettingsButton.Click += async (_, _) => await ShowSettingsAsync();
        CaptureButton.Click += async (_, _) => await CaptureAsync();
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) => _ = ListDevicesAsync();
        Closing += (_, _) => { _health.Stop(); _ = StopAsync(); };
        _health.Tick += (_, _) => ShowHealth();
    }

    private async Task ListDevicesAsync()
    {
        await StopAsync();
        PreviewText.IsVisible = true;
        PreviewText.Text = "Looking for cameras…";
        CameraSources.Found found;
        try { found = await CameraSources.FindAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception ex)
        {
            _devices = [];
            PreviewText.Text = $"Could not list cameras: {ex.Message}";
            return;
        }
        _devices = found.Devices.Where(d => d.Characteristics.Any(c => c.PixelFormat != PixelFormats.Unknown)).ToArray();
        if (found.Problems.Count > 0) StatusText.Text = string.Join("; ", found.Problems);
        _switching = true;
        DeviceBox.ItemsSource = _devices.Select(d => d.Name).ToList();
        int pick = Array.FindIndex(_devices, d => d.Name == _settings.CameraName);
        DeviceBox.SelectedIndex = _devices.Length == 0 ? -1 : Math.Max(0, pick);
        _switching = false;
        if (_devices.Length == 0)
        {
            PreviewText.Text = "No camera found. Plug one in and press Look again.";
            return;
        }
        FillFormats();
    }

    /// <summary>Formats, largest first; formats the library cannot decode are left out.</summary>
    private void FillFormats()
    {
        if (DeviceBox.SelectedIndex < 0 || DeviceBox.SelectedIndex >= _devices.Length) return;
        var d = _devices[DeviceBox.SelectedIndex];
        _formats = d.Characteristics.Where(c => c.PixelFormat != PixelFormats.Unknown)
                    .OrderByDescending(c => c.Width * c.Height)
                    .ThenBy(c => c.IsCompression) // same size: prefer uncompressed
                    .ThenByDescending(c => c.FramesPerSecond.Numerator / (double)Math.Max(1, c.FramesPerSecond.Denominator))
                    .ToArray();
        _switching = true;
        FormatBox.ItemsSource = _formats.Select(Describe).ToList();
        int pick = Array.FindIndex(_formats, c => Describe(c) == _settings.CameraFormat);
        FormatBox.SelectedIndex = _formats.Length == 0 ? -1 : Math.Max(0, pick);
        _switching = false;
        _ = StartAsync();
    }

    private static string Describe(VideoCharacteristics c) =>
        $"{c.Width} × {c.Height}  {c.PixelFormat}  {c.FramesPerSecond.Numerator / (double)Math.Max(1, c.FramesPerSecond.Denominator):0.#} fps";

    private async Task StartAsync()
    {
        await StopAsync();
        if (DeviceBox.SelectedIndex < 0 || FormatBox.SelectedIndex < 0 || FormatBox.SelectedIndex >= _formats.Length) return;
        var d = _devices[DeviceBox.SelectedIndex];
        var c = _formats[FormatBox.SelectedIndex];
        PreviewText.IsVisible = true;
        PreviewText.Text = $"Starting {d.Name}…";
        try
        {
            _frameCount = _shownCount = 0;
            _frameError = null;
            _device = await d.OpenAsync(c, OnFrame);
            await _device.StartAsync();
            _started = DateTime.UtcNow;
            _describe = $"{d.Name}: {Describe(c)}";
            SettingsButton.IsEnabled = _device.HasPropertyPage && OperatingSystem.IsWindows();
            CaptureButton.IsEnabled = true;
            StatusText.Text = _describe;
            _health.Start();
        }
        catch (Exception ex)
        {
            PreviewText.Text = $"Could not start the camera: {ex.Message}";
            CaptureButton.IsEnabled = false;
        }
    }

    private async Task StopAsync()
    {
        var dev = _device;
        _device = null;
        _health.Stop();
        CaptureButton.IsEnabled = false;
        if (dev is null) return;
        try { await dev.StopAsync(); } catch { /* already gone (unplugged) */ }
        try { await dev.DisposeAsync(); } catch { }
    }

    /// <summary>Runs on the camera's thread for every frame.</summary>
    private void OnFrame(PixelBufferScope scope)
    {
        byte[] data = scope.Buffer.CopyImage(); // BMP or JPEG, valid after the scope ends
        Interlocked.Increment(ref _frameCount);
        lock (_gate)
        {
            if (_collecting is { } list && list.Count < _wanted)
            {
                list.Add(data);
                if (list.Count == _wanted) _collected?.TrySetResult(list);
            }
        }
        // Preview at ~8 fps: decoding every frame of a 4K camera would swamp the UI.
        if ((DateTime.UtcNow - _lastPreview).TotalMilliseconds < 120) return;
        _lastPreview = DateTime.UtcNow;
        (int w, int h, byte[] bgra) preview;
        try { preview = PreviewPixels(RgbImage.Decode(data), 1280); }
        catch (Exception ex)
        {
            _frameError = $"The camera sends frames, but they could not be decoded ({ex.Message}). Try another format.";
            return;
        }
        _frameError = null;
        Dispatcher.UIThread.Post(() =>
        {
            var wb = new WriteableBitmap(new PixelSize(preview.w, preview.h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = wb.Lock())
                for (int y = 0; y < preview.h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(preview.bgra, y * preview.w * 4, fb.Address + y * fb.RowBytes, preview.w * 4);
            var old = Preview.Source as Bitmap;
            Preview.Source = wb;
            PreviewText.IsVisible = false;
            _shownCount++;
            old?.Dispose();
        });
    }

    /// <summary>A preview-sized copy (nearest pixel, at most <paramref name="maxWidth"/> wide) as BGRA rows.</summary>
    private static (int w, int h, byte[] bgra) PreviewPixels(RgbImage img, int maxWidth)
    {
        int step = Math.Max(1, (int)Math.Ceiling(img.Width / (double)maxWidth));
        int w = img.Width / step, h = img.Height / step;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = ((y * step) * img.Width + x * step) * 3, d = (y * w + x) * 4;
                px[d] = img.Pixels[s + 2]; px[d + 1] = img.Pixels[s + 1]; px[d + 2] = img.Pixels[s]; px[d + 3] = 255;
            }
        return (w, h, px);
    }

    /// <summary>Once a second: say plainly whether frames are arriving, rather than leaving a black preview.</summary>
    private void ShowHealth()
    {
        if (_device is null) { _health.Stop(); return; }
        double secs = (DateTime.UtcNow - _started).TotalSeconds;
        if (_frameError is { } err) { PreviewText.IsVisible = _shownCount == 0; PreviewText.Text = err; StatusText.Text = err; return; }
        if (_frameCount == 0 && secs > 4)
        {
            PreviewText.IsVisible = true;
            PreviewText.Text = "No picture from this camera yet. Close any other app using it (MakeIt, the Camera app, a browser), or pick another format or camera.";
            StatusText.Text = $"{_describe} · no frames in {secs:0} s";
            return;
        }
        StatusText.Text = $"{_describe} · {_frameCount / Math.Max(secs, 1):0} frames/s";
    }

    private async Task ShowSettingsAsync()
    {
        if (_device is null || TryGetPlatformHandle() is not { } handle) return;
        try { await _device.ShowPropertyPageAsync(handle.Handle); }
        catch (Exception ex) { StatusText.Text = $"Could not open the camera's settings: {ex.Message}"; }
    }

    private async Task CaptureAsync()
    {
        if (_device is null) return;
        int n = (int)(FramesBox.Value ?? 8);
        CaptureButton.IsEnabled = false;
        StatusText.Text = $"Taking {n} frame{(n == 1 ? "" : "s")}…";
        var tcs = new TaskCompletionSource<List<byte[]>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) { _collecting = new List<byte[]>(n); _wanted = n; _collected = tcs; }
        try
        {
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10 + n)));
            if (done != tcs.Task) throw new TimeoutException("The camera stopped sending frames.");
            var frames = tcs.Task.Result;
            var image = await Task.Run(() => RgbImage.Average(frames.Select(RgbImage.Decode).ToList()));

            var d = _devices[DeviceBox.SelectedIndex];
            var c = _formats[FormatBox.SelectedIndex];
            _settings.CameraName = d.Name;
            _settings.CameraFormat = Describe(c);
            _settings.CameraFrames = n;
            _settings.Save();
            await StopAsync();
            Close(new CameraResult(image, d.Name, Describe(c), n));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not take the picture: {ex.Message}";
            CaptureButton.IsEnabled = _device is not null;
        }
        finally
        {
            lock (_gate) { _collecting = null; _collected = null; }
        }
    }
}
