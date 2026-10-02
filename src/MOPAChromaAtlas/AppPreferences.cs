using System.Text.Json;

namespace MOPAChromaAtlas;

/// <summary>
/// Program-wide preferences that are not about the atlas or the cameras: update checking and
/// which version last ran. Kept beside the atlas in the user's profile
/// (%APPDATA%\MOPAChromaAtlas on Windows, ~/.config/MOPAChromaAtlas on Linux and macOS),
/// never in the program folder, so an update cannot touch them. Reading or writing it never
/// throws: a missing or broken file means the defaults.
/// </summary>
public sealed class AppPreferences
{
    /// <summary>Ask GitHub (at most once a day) whether a newer release exists.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>A release the user chose to skip; its bar is not shown again.</summary>
    public string? SkipVersion { get; set; }
    /// <summary>The version that last ran here, so a newer one can offer its release notes once.</summary>
    public string? LastRunVersion { get; set; }
    /// <summary>The version of the safety notice (SafetyWindow.NoticeVersion) this user accepted; 0 = not yet.</summary>
    public int SafetyAccepted { get; set; }
    /// <summary>Where palettes and libraries were last saved; the Save box opens there next time.</summary>
    public string? ExportFolder { get; set; }

    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MOPAChromaAtlas");

    public static string DefaultPath => Path.Combine(Folder, "preferences.json");

    private static AppPreferences? _current;
    public static AppPreferences Current => _current ??= Load();

    public static AppPreferences Load()
    {
        try { return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(DefaultPath)) ?? new(); }
        catch (Exception) { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(DefaultPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* preferences are a convenience */ }
    }
}
