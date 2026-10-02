using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MOPAChroma.Atlas;
using MOPAChroma.Scanning;

namespace MOPAChromaAtlas.Views;

/// <summary>
/// Reads every numbered grid on one scan of a whole sheet. Shows what was found, outlined
/// and numbered on the picture, for the user to check, then returns the readings (every
/// cell as the reader judged it) for the main window to record. Returns null on Cancel.
/// </summary>
public sealed class SheetReadWindow : Window
{
    private readonly AtlasDocument _atlas;
    private readonly RgbImage _img;
    private readonly string _name;
    private readonly PhotoView _photo = new() { EmptyText = "" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly StackPanel _list = new() { Spacing = 4 };
    private readonly CheckBox _scanBox = new() { Content = "This picture is a flatbed scan (fixed, even light)" };
    private readonly Button _save = new() { Content = "Save readings", Classes = { "accent" }, IsEnabled = false };
    private IReadOnlyList<SheetFind> _found = [];
    private int _skipped;

    public SheetReadWindow(AtlasDocument atlas, RgbImage img, string name)
    {
        _atlas = atlas; _img = img; _name = name;
        Title = "Read every grid on a sheet";
        Width = 1100; Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _scanBox.IsChecked = CardCapture.GuessKind(img) == CaptureKind.FlatbedScan;
        ToolTip.SetTip(_scanBox, "A scanner's readings replace camera estimates in the palette and exports. Untick for a photo.");

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close(null);
        _save.Click += (_, _) => Close(Readings());

        _photo.SetPicture(img);
        _photo.DrawOverlay = DrawFinds;

        var side = new StackPanel
        {
            Width = 330, Spacing = 12, Margin = new Thickness(14, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = "Grids found", FontWeight = FontWeight.SemiBold, FontSize = 15 },
                _status,
                new ScrollViewer { Content = _list, MaxHeight = 420 },
                _scanBox,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                    Text = "Only grids engraved by Atlas with corner crosses and a number can be found on a sheet. " +
                           "Older grids: select the grid and use \"Read this grid from an image\". " +
                           "Each cell is saved as the reader judged it; earlier results are kept, and a grid's waiting cells are filled.",
                },
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, _save } };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(14) };
        body.Children.Add(_photo);
        Grid.SetColumn(side, 1); body.Children.Add(side);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(buttons, 1); Grid.SetColumnSpan(buttons, 2); body.Children.Add(buttons);
        Content = body;

        Opened += async (_, _) => await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _status.Text = "Looking for grids… (a large scan can take a little while)";
        try
        {
            void Progress(string s)
            {
                if (s.Contains("skipped")) Interlocked.Increment(ref _skipped);
                Dispatcher.UIThread.Post(() => _status.Text = s);
            }
            _found = await Task.Run(() => SheetReader.FindAll(_img, _atlas, Progress));
        }
        catch (Exception ex)
        {
            _status.Text = "Could not read the sheet: " + ex.Message;
            return;
        }
        _list.Children.Clear();
        foreach (var f in _found.OrderBy(f => f.Grid.Number))
        {
            int colour = f.Grid.Cells.Count(c => f.Reading.At(c.Row, c.Column).Suggested is Outcome.Good or Outcome.Uneven);
            int none = f.Grid.Cells.Count(c => f.Reading.At(c.Row, c.Column).Suggested == Outcome.NoMark);
            var mat = _atlas.Materials.FirstOrDefault(m => m.Id == f.Grid.MaterialId)?.DisplayName ?? f.Grid.MaterialId;
            _list.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Text = $"#{f.Grid.Number}  {f.Grid.Title}\n    {mat}: {f.Grid.Cells.Count} cells, {colour} with colour, {none} no mark" +
                       (f.Detection.TurnDegrees != 0 ? $", {GridDetect.DescribeTurn(f.Detection.TurnDegrees)}" : ""),
            });
        }
        _status.Text = _found.Count switch
        {
            0 => "No numbered grids found. Is the whole sheet in the picture, and were the grids engraved by Atlas with corner crosses?",
            1 => "Found 1 grid. Check its outline on the picture, then save.",
            var n => $"Found {n} grids. Check their outlines on the picture, then save.",
        };
        if (_skipped > 0)
            _status.Text += _skipped == 1
                ? " One other grid was skipped: its number could not be read, or it is not in this atlas."
                : $" {_skipped} other grids were skipped: their numbers could not be read, or they are not in this atlas.";
        _save.IsEnabled = _found.Count > 0;
        _save.Content = _found.Count == 1 ? "Save 1 grid's readings" : $"Save {_found.Count} grids' readings";
        _photo.Redraw();
    }

    private List<Sample> Readings()
    {
        var capture = new CardCapture(_img, _scanBox.IsChecked == true ? CaptureKind.FlatbedScan : CaptureKind.Photo,
                                      _scanBox.IsChecked == true ? "Flatbed scanner" : "Photo", _name);
        var all = new List<Sample>();
        foreach (var f in _found)
            all.AddRange(GridReadout.ToSamples(f.Grid, f.Reading, (r, c) => f.Reading.At(r, c).Suggested, capture,
                                               f.Detection.TurnDegrees, f.Detection.Grid.IsPerspective, $"Sheet read from {_name}"));
        return all;
    }

    private void DrawFinds(Canvas canvas, Func<PointD, Point> toScreen, double scale)
    {
        var colour = Color.Parse("#4FD1FF");
        foreach (var f in _found)
        {
            var g = f.Detection.Grid;
            double h = 0.5;
            var corners = new[] { g.CentreAt(-h, -h), g.CentreAt(-h, g.Columns - 1 + h), g.CentreAt(g.Rows - 1 + h, g.Columns - 1 + h), g.CentreAt(g.Rows - 1 + h, -h) };
            canvas.Children.Add(new Polygon
            {
                Points = corners.Select(toScreen).ToList(),
                Stroke = new SolidColorBrush(colour), StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(30, colour.R, colour.G, colour.B)),
            });
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 24, 30)), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1),
                Child = new TextBlock { Text = $"#{f.Grid.Number}", Foreground = new SolidColorBrush(colour), FontWeight = FontWeight.Bold, FontSize = 14 },
            };
            var at = toScreen(g.CentreAt((g.Rows - 1) / 2.0, (g.Columns - 1) / 2.0));
            Canvas.SetLeft(label, at.X - 18); Canvas.SetTop(label, at.Y - 12);
            canvas.Children.Add(label);
        }
    }
}
