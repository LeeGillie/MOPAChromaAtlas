using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using MOPAChromaAtlas.Updates;

namespace MOPAChromaAtlas.Views;

/// <summary>
/// What's new: the release notes built into the program (CHANGELOG.md), every version, newest
/// first, shown in-app. Offered by the bar the first time a newer version starts, and from the
/// main window's version line at any time. Needs no network; the release page is one click away.
/// </summary>
public sealed class ReleaseNotesWindow : Window
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    public ReleaseNotesWindow(string? updatedFrom = null)
    {
        Title = $"What's new in MOPAChroma Atlas {BuildInfo.Version}";
        Width = 760; Height = 640; MinWidth = 480; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var header = new StackPanel
        {
            Spacing = 2, Margin = new Thickness(18, 12),
            Children =
            {
                new TextBlock { Text = $"MOPAChroma Atlas {BuildInfo.Version}", FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = (updatedFrom is null ? "Release notes" : $"Updated from {updatedFrom}. Release notes")
                           + (BuildInfo.BuildDate.Length > 0 ? $" · built {BuildInfo.BuildDate}" : ""),
                    FontSize = 12, Opacity = 0.7,
                },
            },
        };

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20, 6, 20, 20) };
        var sections = ReleaseNotes.Sections(ReleaseNotes.Markdown);
        if (sections.Count == 0)
            body.Children.Add(Para([new ReleaseNotes.Span("This copy does not carry release notes. The release page on GitHub has them.")]));
        if (sections.Count > 0 && !ReleaseNotes.DescribesThisBuild(ReleaseNotes.Markdown))
            body.Children.Add(Para([new ReleaseNotes.Span(
                $"This copy ({BuildInfo.Version}) was built between releases, so the notes below stop at the last release.", Italic: true)]));
        bool first = true;
        foreach (var (_, text) in sections)
        {
            if (!first) body.Children.Add(new Border { Height = 1, Background = Brushes.Gray, Opacity = 0.3, Margin = new Thickness(0, 10) });
            first = false;
            foreach (var b in ReleaseNotes.Parse(text))
                body.Children.Add(b.Kind switch
                {
                    ReleaseNotes.BlockKind.Heading => HeadingBlock(b),
                    ReleaseNotes.BlockKind.Bullet => Bullet(b),
                    _ => Para(b.Spans),
                });
        }

        var page = new Button { Content = "Release page" };
        ToolTip.SetTip(page, "Open this version's release page on GitHub: downloads, checksums and every change.");
        page.Click += async (_, _) =>
        {
            try { if (GetTopLevel(this) is { } top) await top.Launcher.LaunchUriAsync(new Uri(ReleaseNotes.PageUrl)); }
            catch (Exception) { /* no browser: the address is shown beside the button */ }
        };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();

        var link = new SelectableTextBlock
        {
            Text = ReleaseNotes.PageUrl, FontSize = 11, Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { page, close } };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 10) };
        footer.Children.Add(link);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
    }

    private static Control HeadingBlock(ReleaseNotes.Block b)
    {
        var t = Rich(b.Spans, 16);
        t.FontWeight = FontWeight.SemiBold;
        t.Margin = new Thickness(0, 4, 0, 2);
        return t;
    }

    private static Control Para(IReadOnlyList<ReleaseNotes.Span> spans) => Rich(spans, 13);

    private static Control Bullet(ReleaseNotes.Block b)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(new TextBlock { Text = "•", FontSize = 13, Opacity = 0.6, Margin = new Thickness(6, 0, 10, 0) });
        var t = Rich(b.Spans, 13);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
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
            if (s.Code) { run.FontFamily = Mono; run.FontSize = size - 1; }
            tb.Inlines.Add(run);
        }
        return tb;
    }
}
