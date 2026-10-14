using System.Text.Json;
using System.Text.Json.Serialization;
using LumosUltra.Desktop;
using LumosUltra.Camera;

namespace MOPAChromaAtlas.Capture;

/// <summary>
/// Remembered choices for bringing card images in: the scanner's save folder, the last
/// camera and format. Kept beside the atlas in the user profile; a missing or damaged file
/// just means starting with the defaults.
/// <para>
/// Everything about the laser itself - its address, how its camera's pictures are turned,
/// the camera's calibration - lives in <see cref="LumosSettings"/>, the file every program
/// that talks to the laser shares, so a calibration made in one program serves the others.
/// The members below that start with Lumos read and write that file; they used to be stored
/// here, and a copy from before the shared file existed is carried across once.
/// </para>
/// </summary>
public sealed class CaptureSettings
{
    /// <summary>Folder the scanner software saves into, watched for new scans.</summary>
    public string? WatchFolder { get; set; }
    /// <summary>Whether the folder is watched whenever the importer is open.</summary>
    public bool WatchEnabled { get; set; }
    public string? CameraName { get; set; }
    public string? CameraFormat { get; set; }
    /// <summary>Frames averaged per camera capture.</summary>
    public int CameraFrames { get; set; } = 8;

    /// <summary>The laser's own settings, shared between programs (see the class notes).</summary>
    [JsonIgnore]
    public LumosSettings Lumos { get; private set; } = new();

    [JsonIgnore] public string LumosAddress { get => Lumos.Address; set => Lumos.Address = value; }
    [JsonIgnore] public int LumosQuarterTurns { get => Lumos.QuarterTurns; set => Lumos.QuarterTurns = value; }
    [JsonIgnore] public bool LumosMirror { get => Lumos.Mirror; set => Lumos.Mirror = value; }
    [JsonIgnore] public BedCamera? LumosCamera => Lumos.Camera;
    [JsonIgnore] public string LumosOrientation => Lumos.Orientation;

    /// <summary>
    /// The calibration, if it was made with the picture turned the way it is now and with the
    /// current target (see <see cref="LumosSettings.CurrentCamera"/>).
    /// </summary>
    [JsonIgnore] public BedCamera? CurrentLumosCamera => Lumos.CurrentCamera;

    /// <summary>A laser camera JPEG, decoded and turned upright as set.</summary>
    public RgbImage LumosUpright(byte[] jpeg) => Lumos.Upright(jpeg);

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MOPAChromaAtlas");

    /// <summary>Where camera, Lumos and pasted captures are kept, so every reading stays traceable to an image.</summary>
    public static string CapturesFolder => Path.Combine(Folder, "captures");

    private static string FilePath => Path.Combine(Folder, "capture-settings.json");

    public static CaptureSettings Load()
    {
        CaptureSettings s;
        string? json = null;
        try { json = File.ReadAllText(FilePath); s = JsonSerializer.Deserialize<CaptureSettings>(json) ?? new(); }
        catch { s = new(); }
        s.Lumos = LumosSettings.Exists ? LumosSettings.Load() : Migrate(json);
        return s;
    }

    /// <summary>
    /// The laser settings Atlas kept in its own file before the shared one existed, carried
    /// across so an existing calibration is not lost. Read field by field, so a file from any
    /// older version is fine.
    /// </summary>
    private static LumosSettings Migrate(string? json)
    {
        var l = new LumosSettings();
        if (json is null) return l;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("LumosAddress", out var a) && a.ValueKind == JsonValueKind.String) l.Address = a.GetString() ?? l.Address;
            if (r.TryGetProperty("LumosQuarterTurns", out var q) && q.TryGetInt32(out int qt)) l.QuarterTurns = qt;
            if (r.TryGetProperty("LumosMirror", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False) l.Mirror = m.GetBoolean();
            if (r.TryGetProperty("LumosCamera", out var c) && c.ValueKind == JsonValueKind.Object)
                l.Camera = c.Deserialize<BedCamera>();
            if (r.TryGetProperty("LumosCameraOrientation", out var o) && o.ValueKind == JsonValueKind.String) l.CameraOrientation = o.GetString();
            if (r.TryGetProperty("LumosCameraTargetVersion", out var v) && v.TryGetInt32(out int tv)) l.CameraTargetVersion = tv;
            if (l.Camera is not null) l.Save();
        }
        catch { /* nothing worth carrying across */ }
        return l;
    }

    public void Save()
    {
        Lumos.Save();
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Settings are a convenience; never fail an import over them. */ }
    }
}
