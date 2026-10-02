using System.Text.Json;

namespace MOPAChromaAtlas.Capture;

/// <summary>
/// Remembered choices for bringing card images in: the scanner's save folder, the last
/// camera and format, and the laser's address. Kept beside the atlas in the user profile;
/// a missing or damaged file just means starting with the defaults.
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
    public string LumosAddress { get; set; } = LumosUltra.Client.LumosUltraClient.DefaultUsbBaseAddress;

    /// <summary>
    /// How the laser camera's picture is put upright: quarter turns clockwise, then a mirror.
    /// The Ultra's camera delivers its picture on its side; 3 (a quarter turn anticlockwise)
    /// is the default. Changed with the turn/mirror buttons on the picture.
    /// </summary>
    public int LumosQuarterTurns { get; set; } = 3;
    public bool LumosMirror { get; set; }

    /// <summary>The laser camera's calibration to bed millimetres, and the picture orientation it was made in.</summary>
    public MOPAChroma.Scanning.BedCamera? LumosCamera { get; set; }
    public string? LumosCameraOrientation { get; set; }
    /// <summary>The calibration target layout it was made with (<see cref="MOPAChroma.Scanning.BedCameraTarget.Version"/>).</summary>
    public int LumosCameraTargetVersion { get; set; }

    public string LumosOrientation => $"{((LumosQuarterTurns % 4) + 4) % 4}{(LumosMirror ? "m" : "")}";

    /// <summary>
    /// The calibration, if it was made with the picture turned the way it is now and with the
    /// current target. (Calibrations from the first, symmetric target are not trusted: clicked
    /// in the wrong order they fit perfectly and put jobs on the wrong side of the bed.)
    /// </summary>
    public MOPAChroma.Scanning.BedCamera? CurrentLumosCamera =>
        LumosCamera is { } c && LumosCameraOrientation == LumosOrientation
        && LumosCameraTargetVersion == MOPAChroma.Scanning.BedCameraTarget.Version ? c : null;

    /// <summary>A laser camera JPEG, decoded and turned upright as set.</summary>
    public MOPAChroma.Scanning.RgbImage LumosUpright(byte[] jpeg) =>
        MOPAChroma.Scanning.RgbImage.Decode(jpeg).Oriented(LumosQuarterTurns, LumosMirror);

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MOPAChromaAtlas");

    /// <summary>Where camera, Lumos and pasted captures are kept, so every reading stays traceable to an image.</summary>
    public static string CapturesFolder => Path.Combine(Folder, "captures");

    private static string FilePath => Path.Combine(Folder, "capture-settings.json");

    public static CaptureSettings Load()
    {
        try { return JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Settings are a convenience; never fail an import over them. */ }
    }
}
