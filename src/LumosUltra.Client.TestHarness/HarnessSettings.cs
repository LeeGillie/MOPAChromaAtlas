using System.Text.Json;

namespace LumosUltra.Client.TestHarness;

/// <summary>Small persisted settings for the harness itself - not part of the library,
/// purely a convenience so repeated manual testing doesn't mean retyping the same
/// values every run.</summary>
public sealed class HarnessSettings
{
    public string? FramingFilePath { get; set; }
    public string? FramingMd5 { get; set; }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LumosUltra.Client.TestHarness", "settings.json");

    public static HarnessSettings Load()
    {
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<HarnessSettings>(json) ?? new HarnessSettings();
        }
        catch
        {
            // First run, missing file, or corrupt settings - just start fresh rather than crash a debug tool over it.
            return new HarnessSettings();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
    }
}
