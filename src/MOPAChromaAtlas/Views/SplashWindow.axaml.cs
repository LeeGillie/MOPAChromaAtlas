using Avalonia.Controls;

namespace MOPAChromaAtlas.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {BuildInfo.Version}  ·  {BuildInfo.BuildDate}";
    }
}
