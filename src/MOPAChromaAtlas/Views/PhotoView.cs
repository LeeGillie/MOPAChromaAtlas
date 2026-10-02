using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MOPAChroma.Scanning;

namespace MOPAChromaAtlas.Views;

/// <summary>
/// A picture to look at closely: scroll to zoom about the pointer, drag to move around, with
/// a drawing layer on top in picture coordinates. The owner can claim a left-button drag
/// (to move something drawn on the picture) and hear plain clicks.
/// </summary>
public sealed class PhotoView : Grid
{
    private readonly Canvas _imageLayer = new() { IsHitTestVisible = false };
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly TextBlock _empty = new()
    {
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420,
        FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#8A93A3")),
    };

    private RgbImage? _img;
    private double _zoom = 1;
    private PointD? _centre;
    private enum Mode { None, Pan, Owner }
    private Mode _mode;
    private Point _downScreen;
    private PointD _panCentre;
    private bool _moved;

    /// <summary>Called with the picture point under a left press; return true to take the drag (then DragMoved/DragEnded follow).</summary>
    public Func<PointD, bool>? TryStartDrag { get; set; }
    public Action<PointD>? DragMoved { get; set; }
    public Action<PointD>? DragEnded { get; set; }
    /// <summary>A left click (press and release without moving) at a picture point.</summary>
    public Action<PointD>? Clicked { get; set; }
    /// <summary>Draws on the overlay; the function maps picture points to overlay points.</summary>
    public Action<Canvas, Func<PointD, Point>, double>? DrawOverlay { get; set; }
    /// <summary>The cursor over the picture when nothing is being dragged.</summary>
    public Func<PointD, StandardCursorType>? CursorAt { get; set; }

    public RgbImage? Picture => _img;

    public string EmptyText { get => _empty.Text ?? ""; set => _empty.Text = value; }

    public PhotoView()
    {
        Background = new SolidColorBrush(Color.Parse("#0B0C0F"));
        ClipToBounds = true;
        _imageLayer.Children.Add(_image);
        Children.Add(_imageLayer);
        Children.Add(_overlay);
        Children.Add(_empty);
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerWheelChanged += (_, e) => { if (_img is null) return; ZoomBy(Math.Pow(1.25, e.Delta.Y), e.GetPosition(this)); e.Handled = true; };
        SizeChanged += (_, _) => Redraw();
    }

    public void SetPicture(RgbImage? img, bool keepView = false)
    {
        bool sameSize = _img is not null && img is not null && _img.Width == img.Width && _img.Height == img.Height;
        _img = img;
        (_image.Source as IDisposable)?.Dispose();
        _image.Source = img is null ? null : ToBitmap(img);
        _empty.IsVisible = img is null;
        if (!(keepView && sameSize)) { _zoom = 1; _centre = null; }
        Redraw();
    }

    public void WholePicture() { _zoom = 1; _centre = null; Redraw(); }

    public void ZoomBy(double factor, Point? at = null)
    {
        if (_img is null) return;
        var anchor = at ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        var ip = ToPicture(anchor);
        _zoom = Math.Clamp(_zoom * factor, 0.5, 40);
        double s = Scale;
        _centre = new PointD(ip.X - (anchor.X - Bounds.Width / 2) / s, ip.Y - (anchor.Y - Bounds.Height / 2) / s);
        Redraw();
    }

    /// <summary>Fills the view with a picture rectangle (and a margin of a fifth around it).</summary>
    public void ZoomTo(double x0, double y0, double x1, double y1)
    {
        if (_img is null || Bounds.Width <= 0) return;
        double mx = (x1 - x0) * 0.2, my = (y1 - y0) * 0.2;
        x0 -= mx; x1 += mx; y0 -= my; y1 += my;
        double fit = Math.Min(Bounds.Width / _img.Width, Bounds.Height / _img.Height);
        _zoom = Math.Clamp(Math.Min(Bounds.Width / (x1 - x0), Bounds.Height / (y1 - y0)) / fit, 0.5, 40);
        _centre = new PointD((x0 + x1) / 2, (y0 + y1) / 2);
        Redraw();
    }

    private double Scale => _img is null ? 1 : Math.Min(Bounds.Width / _img.Width, Bounds.Height / _img.Height) * _zoom;

    private (double s, double ox, double oy) View()
    {
        if (_img is null) return (1, 0, 0);
        double s = Scale;
        var c = _centre ?? new PointD(_img.Width / 2.0, _img.Height / 2.0);
        return (s, Bounds.Width / 2 - c.X * s, Bounds.Height / 2 - c.Y * s);
    }

    public PointD ToPicture(Point p) { var (s, ox, oy) = View(); return new PointD((p.X - ox) / s, (p.Y - oy) / s); }
    public Point ToScreen(PointD p) { var (s, ox, oy) = View(); return new Point(ox + p.X * s, oy + p.Y * s); }

    public void Redraw()
    {
        _overlay.Children.Clear();
        if (_img is null) return;
        var (s, ox, oy) = View();
        _image.Width = _img.Width * s;
        _image.Height = _img.Height * s;
        Canvas.SetLeft(_image, ox);
        Canvas.SetTop(_image, oy);
        RenderOptions.SetBitmapInterpolationMode(_image, s >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        DrawOverlay?.Invoke(_overlay, ToScreen, s);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_img is null) return;
        var pt = e.GetCurrentPoint(this);
        _downScreen = pt.Position;
        _moved = false;
        _panCentre = _centre ?? new PointD(_img.Width / 2.0, _img.Height / 2.0);
        _mode = pt.Properties.IsLeftButtonPressed && TryStartDrag?.Invoke(ToPicture(pt.Position)) == true ? Mode.Owner : Mode.Pan;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_img is null) return;
        var p = e.GetPosition(this);
        if (_mode != Mode.None && Math.Abs(p.X - _downScreen.X) + Math.Abs(p.Y - _downScreen.Y) > 3) _moved = true;
        switch (_mode)
        {
            case Mode.Pan when _moved:
                double s = Scale;
                _centre = new PointD(_panCentre.X - (p.X - _downScreen.X) / s, _panCentre.Y - (p.Y - _downScreen.Y) / s);
                Redraw();
                break;
            case Mode.Owner:
                DragMoved?.Invoke(ToPicture(p));
                break;
            case Mode.None:
                Cursor = new Cursor(CursorAt?.Invoke(ToPicture(p)) ?? StandardCursorType.Cross);
                break;
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var mode = _mode;
        _mode = Mode.None;
        e.Pointer.Capture(null);
        if (_img is null) return;
        var p = ToPicture(e.GetPosition(this));
        if (mode == Mode.Owner) DragEnded?.Invoke(p);
        else if (mode == Mode.Pan && !_moved && e.InitialPressMouseButton == MouseButton.Left) Clicked?.Invoke(p);
    }

    private static WriteableBitmap ToBitmap(RgbImage img)
    {
        var wb = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = wb.Lock();
        var row = new byte[img.Width * 4];
        for (int y = 0; y < img.Height; y++)
        {
            int s = y * img.Width * 3;
            for (int x = 0; x < img.Width; x++)
            {
                row[x * 4] = img.Pixels[s + x * 3 + 2];
                row[x * 4 + 1] = img.Pixels[s + x * 3 + 1];
                row[x * 4 + 2] = img.Pixels[s + x * 3];
                row[x * 4 + 3] = 255;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
        }
        return wb;
    }
}
