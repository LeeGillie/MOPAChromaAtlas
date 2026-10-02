using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using MOPAChromaAtlas.Updates;

namespace MOPAChromaAtlas.Views;

/// <summary>
/// The version line and the update bar in the header.
///
/// Same behaviour as DepthView. The check runs once when the window opens (at most once a day
/// against GitHub, otherwise from a cache) and "Check for updates" runs it on demand. A check
/// that fails is silent when automatic, and a one-line status when the user asked. The bar offers
/// What's new, Skip this version and Update now; a copy that cannot replace itself (built from
/// source, renamed, unpacked somewhere read-only) gets the reason and the download page instead.
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush UpdateTextBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xEB, 0xD7));
    private static readonly IBrush UpdateErrorBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xA0, 0x96));

    /// <summary>The user guide's file name inside a release folder.</summary>
    public const string GuidePdfName = "MOPAChroma-Atlas-User-Guide.pdf";
    private const string GuideOnline = "https://github.com/" + UpdateService.Repo + "/blob/main/docs/USER-GUIDE.md";

    private UpdateInfo? _update;
    private bool _updating;
    private bool _justUpdated;
    private ReleaseNotesWindow? _notes;

    private void WireUpdates()
    {
        VersionText.Text = $"Version {BuildInfo.Version}" + (BuildInfo.BuildDate.Length > 0 ? $" · built {BuildInfo.BuildDate}" : "");
        WhatsNewButton.Click += (_, _) => OpenReleaseNotes();
        ReportButton.Click += async (_, _) => await OpenUrlAsync("https://github.com/" + UpdateService.Repo + "/issues");
        SafetyButton.Click += async (_, _) => await new SafetyWindow(mustAccept: false).ShowDialog<bool>(this);
        GuideButton.Click += async (_, _) => await OpenGuideAsync();
        CheckUpdatesButton.Click += async (_, _) =>
        {
            Status("Checking for updates…");
            var info = await UpdateService.CheckAsync(force: true);
            if (info.Error is not null) Status("Could not check for updates just now (no connection to GitHub?). Nothing else is affected.");
            else if (!info.Newer) Status($"You have the latest version ({BuildInfo.Version}).");
            else Status($"MOPAChroma Atlas {info.Latest} is available.");
            UpdateService.Announce(info, fromUser: true);
        };

        UpdateService.Checked += OnUpdateChecked;
        Closed += (_, _) => UpdateService.Checked -= OnUpdateChecked;

        UpdateNotesButton.Click += async (_, _) =>
        {
            if (_justUpdated) OpenReleaseNotes(ReleaseNotes.UpdatedFrom);
            else await OpenUrlAsync(_update?.NotesUrl ?? UpdateService.ReleasesPage);
        };
        UpdateDownloadButton.Click += async (_, _) => await OpenUrlAsync(_update?.NotesUrl ?? UpdateService.ReleasesPage);
        UpdateHideButton.Click += (_, _) => UpdateBar.IsVisible = false;
        UpdateSkipButton.Click += (_, _) =>
        {
            AppPreferences.Current.SkipVersion = _update?.Latest;
            AppPreferences.Current.Save();
            UpdateBar.IsVisible = false;
        };
        UpdateInstallButton.Click += async (_, _) => await InstallUpdateAsync();

        // Not on a screenshot or demo-window run: those must come out the same every time.
        Opened += async (_, _) =>
        {
            // The safety notice comes first, once (and again when it changes). Not on a
            // screenshot run, which must come out the same every time and has no one to click.
            if (Program.Options.ScreenshotPath is null && !SafetyWindow.Accepted)
            {
                bool ok = await new SafetyWindow(mustAccept: true).ShowDialog<bool>(this);
                if (!ok) { Close(); return; }
            }
            try
            {
                var opts = Program.Options;
                bool testFeed = opts.UpdateFeed is not null;
                bool quiet = opts.ScreenshotPath is not null || opts.NoUpdateCheck;

                if (ReleaseNotes.JustUpdated && (!quiet || testFeed)) ShowJustUpdated();
                else if (!quiet) ReleaseNotes.MarkSeen();

                if (!testFeed && (quiet || !AppPreferences.Current.CheckForUpdates)) return;
                var info = await UpdateService.CheckAsync();
                UpdateService.Announce(info, fromUser: false);
            }
            catch (Exception) { /* an update check must never disturb the program */ }
        };
    }

    private void OnUpdateChecked(UpdateInfo info, bool fromUser)
    {
        if (_updating) return;
        _update = info;
        RenderUpdateBar(fromUser);
    }

    private void RenderUpdateBar(bool fromUser)
    {
        var u = _update;
        bool show = u is { Newer: true, Error: null }
                    && (fromUser || (AppPreferences.Current.CheckForUpdates && u.Latest != AppPreferences.Current.SkipVersion));
        if (!show || u is null)
        {
            if (!_justUpdated) UpdateBar.IsVisible = false;
            return;
        }

        _justUpdated = false;
        ToolTip.SetTip(UpdateNotesButton, "Open the release notes for the new version in your web browser.");
        UpdateText.Foreground = UpdateTextBrush;
        UpdateText.Text = u.CanInstall
            ? $"MOPAChroma Atlas {u.Latest} is available. You have {u.Current}."
            : $"MOPAChroma Atlas {u.Latest} is available. You have {u.Current}. {u.InstallBlocker}";
        UpdateProgress.IsVisible = false;
        UpdateNotesButton.IsVisible = true;
        UpdateSkipButton.IsVisible = true;
        UpdateInstallButton.IsVisible = u.CanInstall;
        UpdateDownloadButton.IsVisible = !u.CanInstall;
        UpdateHideButton.IsVisible = true;
        UpdateBar.IsVisible = true;
    }

    /// <summary>The bar after an update: this version is new here, and its notes are one click away. Offered once.</summary>
    private void ShowJustUpdated()
    {
        _justUpdated = true;
        string from = ReleaseNotes.UpdatedFrom is { } f ? $" (you had {f})" : "";
        UpdateText.Foreground = UpdateTextBrush;
        UpdateText.Text = $"MOPAChroma Atlas has been updated to {BuildInfo.Version}{from}. See what is new?";
        ToolTip.SetTip(UpdateNotesButton, "Show the release notes. They are built into the program, so this works offline.");
        UpdateProgress.IsVisible = false;
        UpdateNotesButton.IsVisible = true;
        UpdateSkipButton.IsVisible = false;
        UpdateInstallButton.IsVisible = false;
        UpdateDownloadButton.IsVisible = false;
        UpdateHideButton.IsVisible = true;
        UpdateBar.IsVisible = true;
        ReleaseNotes.MarkSeen();
    }

    internal void OpenReleaseNotes(string? updatedFrom = null)
    {
        if (_notes is not null) { _notes.Activate(); return; }
        _notes = new ReleaseNotesWindow(updatedFrom);
        _notes.Closed += (_, _) => _notes = null;
        _notes.Show(this);
        if (_justUpdated) UpdateBar.IsVisible = false;
    }

    /// <summary>
    /// The PDF that ships beside the program (beside the .app on a Mac); in a copy built from
    /// source, the one built into docs/; otherwise the guide on GitHub.
    /// </summary>
    private async Task OpenGuideAsync()
    {
        string? pdf = null;
        var layout = UpdateService.CurrentLayout();
        if (layout is not null && File.Exists(Path.Combine(layout.Folder, GuidePdfName))) pdf = Path.Combine(layout.Folder, GuidePdfName);
        else
        {
            // A source build: walk up from the program to the repository's docs folder.
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null && pdf is null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "docs", GuidePdfName))) pdf = Path.Combine(d.FullName, "docs", GuidePdfName);
        }
        if (pdf is not null)
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is { } top
                    && await top.StorageProvider.TryGetFileFromPathAsync(new Uri(pdf)) is { } file
                    && await top.Launcher.LaunchFileAsync(file))
                    return;
            }
            catch (Exception) { /* fall through to the online guide */ }
        }
        await OpenUrlAsync(GuideOnline);
    }

    private void ShowUpdateMessage(string text, bool error, bool busy)
    {
        UpdateText.Text = text;
        UpdateText.Foreground = error ? UpdateErrorBrush : UpdateTextBrush;
        UpdateProgress.IsVisible = busy;
        UpdateNotesButton.IsVisible = !busy;
        UpdateSkipButton.IsVisible = false;
        UpdateInstallButton.IsVisible = false;
        UpdateDownloadButton.IsVisible = error;
        UpdateHideButton.IsVisible = !busy;
        UpdateBar.IsVisible = true;
    }

    private async Task InstallUpdateAsync()
    {
        if (_updating || _update is null) return;
        var layout = UpdateService.CurrentLayout();
        if (layout is null) { RenderUpdateBar(true); return; }

        _updating = true;
        string target = _update.Latest ?? "";
        UpdateProgress.IsIndeterminate = true;
        ShowUpdateMessage($"Checking for MOPAChroma Atlas {target}…", error: false, busy: true);

        var progress = new Progress<(string Stage, double? Fraction)>(p =>
        {
            UpdateText.Text = $"{p.Stage}: MOPAChroma Atlas {target}…";
            UpdateProgress.IsIndeterminate = p.Fraction is null;
            if (p.Fraction is double f) UpdateProgress.Value = f * 100;
        });

        try
        {
            // Ask again rather than trust a cached answer: the release could have been replaced.
            var fresh = await UpdateService.CheckAsync(force: true);
            if (fresh.Error is not null) throw new UpdateException(fresh.Error);
            if (!fresh.Newer) throw new UpdateException("This is already the latest version.");
            if (!fresh.CanInstall) throw new UpdateException(fresh.InstallBlocker ?? "This copy cannot be updated in place.");
            target = fresh.Latest ?? target;
            await Task.Run(() => UpdateService.InstallAsync(fresh, layout, progress));
        }
        catch (UpdateException ex)
        {
            _updating = false;
            ShowUpdateMessage($"The update was not installed: {ex.Message} Nothing was changed.", error: true, busy: false);
            return;
        }
        catch (Exception ex)
        {
            _updating = false;
            ShowUpdateMessage($"The update stopped unexpectedly: {ex.Message} Nothing was changed.", error: true, busy: false);
            return;
        }

        ShowUpdateMessage($"Installed MOPAChroma Atlas {target}. Restarting…", error: false, busy: true);
        try
        {
            Process.Start(UpdateService.RestartStartInfo(layout, Environment.GetCommandLineArgs().Skip(1)));
        }
        catch (Exception ex)
        {
            _updating = false;
            ShowUpdateMessage($"MOPAChroma Atlas {target} is installed, but did not start by itself ({ex.Message}). Start it again the usual way.",
                              error: true, busy: false);
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private async Task OpenUrlAsync(string url)
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is { } top) await top.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception) { Status("Could not open a browser. The address is " + url); }
    }
}
