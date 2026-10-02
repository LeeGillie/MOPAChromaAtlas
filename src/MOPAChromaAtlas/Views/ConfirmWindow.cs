using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MOPAChromaAtlas.Views;

/// <summary>A plain yes/no question (or a notice with one button) in its own small window.</summary>
public sealed class ConfirmWindow : Window
{
    private ConfirmWindow(string title, string text, string yes, bool canCancel = true)
    {
        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var yesButton = new Button { Content = yes, Classes = { "accent" } };
        var noButton = new Button { Content = "Cancel", IsVisible = canCancel };
        yesButton.Click += (_, _) => Close(true);
        noButton.Click += (_, _) => Close(false);
        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 13 },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { noButton, yesButton } },
            },
        };
    }

    public static Task<bool> AskAsync(Window owner, string title, string text, string yes) =>
        new ConfirmWindow(title, text, yes).ShowDialog<bool>(owner);

    /// <summary>Something the user must see (not just a status line), with an OK button.</summary>
    public static Task TellAsync(Window owner, string title, string text) =>
        new ConfirmWindow(title, text, "OK", canCancel: false).ShowDialog<bool>(owner);
}
