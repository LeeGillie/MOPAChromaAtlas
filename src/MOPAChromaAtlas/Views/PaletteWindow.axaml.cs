using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Export;
using LumosUltra.Jobs;
namespace MOPAChromaAtlas.Views;

/// <summary>
/// The colours the atlas can make for one material, source and lens (Good cells with a
/// colour, one per recipe, repeated ones first), and the exports built from them.
/// </summary>
public partial class PaletteWindow : Window
{
    private readonly Material _material;
    private readonly SourceProfile _source;
    private readonly LensProfile _lens;
    private readonly IReadOnlyList<PaletteEntry> _palette;

    public PaletteWindow() : this(new Material { Id = "preview", Name = "Preview" }, SourceProfile.Presets[0], LensProfile.Presets[0], []) { }

    public PaletteWindow(Material material, SourceProfile source, LensProfile lens, IReadOnlyList<Sample> samples)
    {
        InitializeComponent();
        _material = material;
        _source = source;
        _lens = lens;
        _palette = Palette.Build(samples, material.Id, source.Id, lens.Id);
        HeaderText.Text = $"{material.DisplayName} · {source.Name} · {lens.Name}";
        int verified = _palette.Count(p => p.Verified);
        SummaryText.Text = $"{_palette.Count} colour{(_palette.Count == 1 ? "" : "s")}, {verified} repeated and confirmed (✓). " +
                           "Only cells read as Good with a colour are here: no-mark, uneven and damaged cells are left out. " +
                           "Each colour shows its latest reading (an instrument reading, if any, comes first).";
        ShowUnverifiedBox.IsCheckedChanged += (_, _) => Fill();
        LightBurnButton.Click += async (_, _) =>
        {
            try { await SaveLightBurnAsync(); }
            catch (Exception ex) { LightBurnText.Text = "Could not save: " + ex.Message; }
        };
        MakeItButton.Click += async (_, _) =>
        {
            try { await SaveMakeItAsync(); }
            catch (Exception ex) { MakeItText.Text = "Could not save: " + ex.Message; }
        };
        MakeItFolderButton.Click += (_, _) => ShowInFolder(_makeItPath);
        MakeItMaterialsButton.Click += async (_, _) =>
        {
            try { await SaveMakeItMaterialsAsync(); }
            catch (Exception ex) { MakeItMaterialsText.Text = "Could not save: " + ex.Message; }
        };
        MakeItMaterialsFolderButton.Click += (_, _) => ShowInFolder(_materialsPath);
        LightBurnFolderButton.Click += (_, _) => ShowInFolder(_lightBurnPath);
        CloseButton.Click += (_, _) => Close();
        // Ticked only when there is something repeated to export; otherwise the exports would be empty.
        VerifiedOnlyBox.IsChecked = verified > 0;
        VerifiedOnlyBox.Content = $"Repeated (verified) colours only: {verified} of {_palette.Count}";
        VerifiedOnlyBox.IsCheckedChanged += (_, _) => ShowExportCount();
        ShowExportCount();
        Fill();
    }

    /// <summary>How many colours the exports will hold; the save buttons are off when none.</summary>
    private void ShowExportCount()
    {
        bool only = VerifiedOnlyBox.IsChecked == true;
        int n = only ? _palette.Count(p => p.Verified) : _palette.Count;
        MakeItButton.IsEnabled = MakeItMaterialsButton.IsEnabled = LightBurnButton.IsEnabled = n > 0;
        ExportCountText.Text = n > 0
            ? $"The exports will hold {n} colour{(n == 1 ? "" : "s")}{(only ? ", each made the same on two cards" : ", including colours made only once")}."
            : _palette.Count == 0
                ? "No colours to export yet."
                : $"Nothing to export: none of your {_palette.Count} colours has been repeated on a second card yet. Untick the box above to export all of them.";
    }

    private void Fill()
    {
        var shown = _palette.Where(p => p.Verified || ShowUnverifiedBox.IsChecked == true).ToList();
        EmptyText.IsVisible = shown.Count == 0;
        Swatches.ItemsSource = shown.Select(Card).ToList();
    }

    private static Control Card(PaletteEntry e)
    {
        var rgb = ColorConvert.ToSrgb(e.Color);
        var s = e.Settings;
        var panel = new StackPanel { Spacing = 2, Width = 168 };
        panel.Children.Add(new Border
        {
            Height = 72, CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B)),
            BorderBrush = new SolidColorBrush(Color.Parse("#40FFFFFF")), BorderThickness = new Thickness(1),
        });
        panel.Children.Add(new TextBlock { Text = (e.Verified ? "✓ " : "") + e.Name, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        panel.Children.Add(new TextBlock
        {
            Text = $"{s.PowerPercent:0.#}% · {s.SpeedMmPerS:0} mm/s\n{s.FrequencyKHz:0.#} kHz · {s.PulseWidthNs:0.#} ns\n" +
                   $"{s.LineDensityPerCm:0}/cm · {s.Passes} pass{(s.Passes == 1 ? "" : "es")}",
            FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#B8C0CC")),
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{e.Id} · {Method(e.Method)}{(e.Readings > 1 ? $" · {e.Readings} readings" : "")}",
            FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#7F8896")),
        });
        var card = new Border { Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8), Child = panel, Background = new SolidColorBrush(Color.Parse("#171A1F")), CornerRadius = new CornerRadius(6) };
        ToolTip.SetTip(card, $"{e.Name}  {e.Hex}\n{e.Color}\n" +
                             $"{Repeatability.Label(e.Repeat.Status)}{(e.Repeat.BestDeltaE00 is { } d ? $" (ΔE00 {d:0.0})" : "")}\n" +
                             $"From cell {e.Best.CellLabel}, read by {Method(e.Method)}");
        return card;
    }

    private static string Method(MeasureMethod m) => m switch
    {
        MeasureMethod.Estimated => "photo",
        MeasureMethod.Scanner => "scan",
        MeasureMethod.CalibratedImage => "calibrated image",
        MeasureMethod.Instrument => "instrument",
        _ => m.ToString(),
    };

    private string? _makeItPath, _lightBurnPath, _materialsPath;

    private async Task SaveMakeItMaterialsAsync()
    {
        bool verifiedOnly = VerifiedOnlyBox.IsChecked == true;
        var now = DateTimeOffset.Now;
        var result = MakeItMaterials.Write(_palette, _material, _source, _lens, verifiedOnly, now);
        if (result.Entries == 0) { MakeItMaterialsText.Text = "No colours to export."; return; }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save MakeIt materials: choose a folder",
            SuggestedFileName = MakeItMaterials.FileName(_material, _source, _lens, verifiedOnly, now),
            SuggestedStartLocation = await ExportStartAsync(),
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("MakeIt materials") { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await File.WriteAllTextAsync(path, result.Json);
        Remember(path);
        _materialsPath = path;
        MakeItMaterialsFolderButton.IsVisible = true;
        MakeItMaterialsText.Text = $"Saved {result.Entries} colour{(result.Entries == 1 ? "" : "s")} as MakeIt materials to\n{path}\n" +
                                   "In MakeIt: Material preview, Import, choose that file.";
        StatusText.Text = "MakeIt materials saved.";
    }

    /// <summary>
    /// Where the Save box opens: the folder used last time, else Documents\MOPAChroma Atlas
    /// (made if needed), so exports land somewhere easy to find rather than wherever the
    /// last program left the box.
    /// </summary>
    private async Task<IStorageFolder?> ExportStartAsync()
    {
        try
        {
            string? folder = AppPreferences.Current.ExportFolder;
            if (folder is null || !Directory.Exists(folder))
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MOPAChroma Atlas");
                Directory.CreateDirectory(folder);
            }
            return await StorageProvider.TryGetFolderFromPathAsync(folder);
        }
        catch (Exception) { return null; }
    }

    private static string PagePath(string first, int page) =>
        Path.Combine(Path.GetDirectoryName(first) ?? "", $"{Path.GetFileNameWithoutExtension(first)}_{page}{Path.GetExtension(first)}");

    private static void Remember(string path)
    {
        AppPreferences.Current.ExportFolder = Path.GetDirectoryName(path);
        AppPreferences.Current.Save();
    }

    /// <summary>
    /// Opens the file's folder with the file selected (Windows, macOS), or the folder (Linux).
    /// Falls back to asking the system to open the folder, and says so if neither works.
    /// </summary>
    private async void ShowInFolder(string? path)
    {
        if (path is null || Path.GetDirectoryName(path) is not { } dir) return;
        try
        {
            var psi = OperatingSystem.IsWindows()
                ? new System.Diagnostics.ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = true }
                : OperatingSystem.IsMacOS()
                ? new System.Diagnostics.ProcessStartInfo("open") { ArgumentList = { "-R", path } }
                : new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { dir } };
            if (System.Diagnostics.Process.Start(psi) is not null) return;
        }
        catch (Exception) { /* try the launcher */ }
        try
        {
            if (await StorageProvider.TryGetFolderFromPathAsync(dir) is { } folder && await Launcher.LaunchFileAsync(folder)) return;
        }
        catch (Exception) { }
        StatusText.Text = "Could not open the folder. The file is at " + path;
    }

    private async Task SaveMakeItAsync()
    {
        bool verifiedOnly = VerifiedOnlyBox.IsChecked == true;
        var now = DateTimeOffset.Now;
        var result = MakeItPalette.Write(_palette, _material, _source, _lens, verifiedOnly, now);
        if (result.Entries == 0)
        {
            MakeItText.Text = verifiedOnly
                ? "No repeated colours yet, so the project would be empty. Untick 'Repeated colours only' to include colours made once."
                : "No colours yet.";
            return;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save MakeIt palette project: choose a folder",
            SuggestedFileName = MakeItPalette.FileName(_material, _source, _lens, verifiedOnly, now),
            SuggestedStartLocation = await ExportStartAsync(),
            DefaultExtension = "wws",
            FileTypeChoices = [new FilePickerFileType("MakeIt project") { Patterns = ["*.wws"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            // More colours than fit in the lens field: page 2 onwards beside the first ("…_2.wws").
            for (int i = 0; i < result.Projects.Count; i++)
                await File.WriteAllTextAsync(i == 0 ? path : PagePath(path, i + 1), result.Projects[i]);
            await File.WriteAllTextAsync(Path.ChangeExtension(path, ".txt"), result.Legend);
            Remember(path);
            _makeItPath = path;
            MakeItFolderButton.IsVisible = true;
            MakeItText.Text = $"Saved {result.Entries} colour square{(result.Entries == 1 ? "" : "s")} to\n{path}\n" +
                              (result.Projects.Count > 1 ? $"They fill {result.Projects.Count} pages, so the others are beside it: {string.Join(", ", Enumerable.Range(2, result.Projects.Count - 1).Select(n => Path.GetFileName(PagePath(path, n))))}.\n" : "") +
                              $"with a list of them beside it ({Path.GetFileName(Path.ChangeExtension(path, ".txt"))}). " +
                              "In MakeIt: File, Open, and choose that file." +
                              (result.PulseWidthsSnapped > 0 ? $" {result.PulseWidthsSnapped} pulse width{(result.PulseWidthsSnapped == 1 ? " was" : "s were")} rounded to ones the source accepts." : "");
            StatusText.Text = "MakeIt palette saved.";
        }
        catch (Exception ex) { MakeItText.Text = $"Could not save: {ex.Message}"; }
    }

    private async Task SaveLightBurnAsync()
    {
        bool verifiedOnly = VerifiedOnlyBox.IsChecked == true;
        var now = DateTimeOffset.Now;
        var result = LightBurnLibrary.Write(_palette, _material, _source, _lens, verifiedOnly, now);
        if (result.Entries == 0)
        {
            LightBurnText.Text = verifiedOnly
                ? "No repeated colours yet, so the library would be empty. Untick 'Repeated colours only' to include colours made once."
                : "No colours yet.";
            return;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save LightBurn material library: choose a folder",
            SuggestedFileName = LightBurnLibrary.FileName(_material, _source, _lens, verifiedOnly, now),
            SuggestedStartLocation = await ExportStartAsync(),
            DefaultExtension = "clb",
            FileTypeChoices = [new FilePickerFileType("LightBurn material library") { Patterns = ["*.clb"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await File.WriteAllTextAsync(path, result.Clb);
            await File.WriteAllTextAsync(Path.ChangeExtension(path, ".json"), result.SidecarJson);
            Remember(path);
            _lightBurnPath = path;
            LightBurnFolderButton.IsVisible = true;
            LightBurnText.Text = $"Saved {result.Entries} colour{(result.Entries == 1 ? "" : "s")} to\n{path}\nwith its .json record beside it." +
                                 (result.PulseWidthsSnapped > 0 ? $" {result.PulseWidthsSnapped} pulse width{(result.PulseWidthsSnapped == 1 ? " was" : "s were")} rounded to ones the source accepts." : "");
            StatusText.Text = "LightBurn library saved.";
        }
        catch (Exception ex) { LightBurnText.Text = $"Could not save: {ex.Message}"; }
    }
}
