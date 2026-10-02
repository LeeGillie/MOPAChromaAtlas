using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using MOPAChromaAtlas.Updates;

namespace MOPAChromaAtlas.Views;

/// <summary>
/// The safety notice and disclaimer (SAFETY.md, built into the program). Shown once, before
/// anything else, when it has not been accepted yet (or a newer notice has), with Accept and
/// Quit; afterwards from the main window's "Safety" link, just to read.
/// </summary>
public sealed class SafetyWindow : Window
{
    /// <summary>Raise when SAFETY.md changes in substance, so everyone accepts the new text once.</summary>
    public const int NoticeVersion = 1;

    private const string ResourceName = "MOPAChromaAtlas.SAFETY.md";

    public static string Markdown
    {
        get
        {
            try
            {
                using var s = typeof(SafetyWindow).Assembly.GetManifestResourceStream(ResourceName);
                if (s is not null) return new StreamReader(s).ReadToEnd();
            }
            catch (Exception) { }
            return "# Safety\n\nMOPAChroma Atlas controls a Class 4 laser. Safety is your responsibility. " +
                   "The software is provided as is, without warranty of any kind (see LICENSE).";
        }
    }

    public static bool Accepted => AppPreferences.Current.SafetyAccepted >= NoticeVersion;

    /// <param name="mustAccept">First run: Accept or Quit. Otherwise only Close.</param>
    public SafetyWindow(bool mustAccept)
    {
        Title = "MOPAChroma Atlas — safety and disclaimer";
        Width = 720; Height = 680; MinWidth = 480; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(22, 16, 22, 20) };
        foreach (var b in ReleaseNotes.Parse(string.Join('\n', Markdown.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim() != "---"))))
            body.Children.Add(Block(b));

        var footer = new StackPanel { Spacing = 10, Margin = new Thickness(18, 12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (mustAccept)
        {
            var tick = new CheckBox { Content = "I have read this. I understand the risks and accept that safety, and the use of this software, are my responsibility.", FontSize = 13 };
            var accept = new Button { Content = "Accept", Classes = { "accent" }, IsEnabled = false };
            var quit = new Button { Content = "Quit" };
            tick.IsCheckedChanged += (_, _) => accept.IsEnabled = tick.IsChecked == true;
            accept.Click += (_, _) =>
            {
                AppPreferences.Current.SafetyAccepted = NoticeVersion;
                AppPreferences.Current.Save();
                Close(true);
            };
            quit.Click += (_, _) => Close(false);
            buttons.Children.Add(quit);
            buttons.Children.Add(accept);
            footer.Children.Add(tick);
        }
        else
        {
            var close = new Button { Content = "Close" };
            close.Click += (_, _) => Close(true);
            buttons.Children.Add(close);
        }
        footer.Children.Add(buttons);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
    }

    private static Control Block(ReleaseNotes.Block b)
    {
        if (b.Kind == ReleaseNotes.BlockKind.Heading)
        {
            var h = Rich(b.Spans, 16);
            h.FontWeight = FontWeight.SemiBold;
            h.Margin = new Thickness(0, 6, 0, 0);
            return h;
        }
        if (b.Kind == ReleaseNotes.BlockKind.Bullet)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(new TextBlock { Text = "•", FontSize = 13, Opacity = 0.6, Margin = new Thickness(6, 0, 10, 0) });
            var t = Rich(b.Spans, 13);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }
        return Rich(b.Spans, 13);
    }

    private static SelectableTextBlock Rich(IReadOnlyList<ReleaseNotes.Span> spans, double size)
    {
        var tb = new SelectableTextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.45 };
        tb.Inlines ??= new InlineCollection();
        foreach (var s in spans)
        {
            var run = new Run(s.Text);
            if (s.Bold) run.FontWeight = FontWeight.SemiBold;
            if (s.Italic) run.FontStyle = FontStyle.Italic;
            tb.Inlines.Add(run);
        }
        return tb;
    }
}
