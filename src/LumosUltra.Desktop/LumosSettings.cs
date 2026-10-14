using System.Text.Json;
using LumosUltra.Camera;

namespace LumosUltra.Desktop;

/// <summary>
/// What every program that talks to the laser needs to remember about it: its address, how
/// its camera's pictures are turned upright, and the camera's calibration to the bed. These
/// are facts about the machine, not about any one program, so they live in one file shared
/// by all of them (<c>%APPDATA%\LumosUltra\settings.json</c>, the per-user application data
/// folder on macOS and Linux): calibrate the camera once, in whichever program, and the
/// others know where a job will land. A missing or damaged file just means starting with
/// the defaults.
/// </summary>
public sealed class LumosSettings
{
    public string Address { get; set; } = LumosUltra.Client.LumosUltraClient.DefaultUsbBaseAddress;

    /// <summary>
    /// How the laser camera's picture is put upright: quarter turns clockwise, then a mirror.
    /// The Ultra's camera delivers its picture on its side; 3 (a quarter turn anticlockwise)
    /// is the default. Changed with the turn/mirror buttons on the picture.
    /// </summary>
    public int QuarterTurns { get; set; } = 3;
    public bool Mirror { get; set; }

    /// <summary>The camera's calibration to bed millimetres, and the picture orientation it was made in.</summary>
    public BedCamera? Camera { get; set; }
    public string? CameraOrientation { get; set; }
    /// <summary>The calibration target layout it was made with (<see cref="BedCameraTarget.Version"/>).</summary>
    public int CameraTargetVersion { get; set; }

    public string Orientation => $"{((QuarterTurns % 4) + 4) % 4}{(Mirror ? "m" : "")}";

    /// <summary>
    /// The calibration, if it was made with the picture turned the way it is now and with the
    /// current target. (Calibrations from the first, symmetric target are not trusted: clicked
    /// in the wrong order they fit perfectly and put jobs on the wrong side of the bed.)
    /// </summary>
    public BedCamera? CurrentCamera =>
        Camera is { } c && CameraOrientation == Orientation && CameraTargetVersion == BedCameraTarget.Version ? c : null;

    /// <summary>A laser camera JPEG, decoded and turned upright as set.</summary>
    public RgbImage Upright(byte[] jpeg) => RgbImage.Decode(jpeg).Oriented(QuarterTurns, Mirror);

    /// <summary>Records a calibration made with the picture as it is now turned.</summary>
    public void SetCamera(BedCamera camera)
    {
        Camera = camera;
        CameraOrientation = Orientation;
        CameraTargetVersion = BedCameraTarget.Version;
    }

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LumosUltra");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static LumosSettings Load()
    {
        try { return JsonSerializer.Deserialize<LumosSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    /// <summary>Whether the shared file exists at all (so a program can carry its own older settings across once).</summary>
    public static bool Exists => File.Exists(FilePath);

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* Settings are a convenience; never fail a job over them. */ }
    }
}
