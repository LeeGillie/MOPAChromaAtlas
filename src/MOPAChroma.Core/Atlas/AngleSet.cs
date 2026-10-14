using MOPAChroma.Color;
using LumosUltra.Jobs;
namespace MOPAChroma.Atlas;

/// <summary>
/// One cell's colour at one combination of view, light and card angles, from the capture
/// box (MOPAChroma Capture).
/// </summary>
/// <param name="TurnDeg">Turntable angle (the card turned about its own face).</param>
/// <param name="TiltDeg">Cradle tilt, as measured from the markers.</param>
/// <param name="Led">Which LED (0-7).</param>
/// <param name="ViewDeg">Angle between the card's normal and the direction to the camera, at this cell.</param>
/// <param name="LightDeg">Angle between the card's normal and the direction to the LED.</param>
/// <param name="HalfDeg">Angle between the normal and the half-way vector: 0 is the mirror reflection of the LED.</param>
/// <param name="LightAzimuthDeg">Direction the light comes from, in the card's plane, from the card's long side (fill lines at 0 run along it).</param>
/// <param name="ViewAzimuthDeg">The same for the camera.</param>
/// <param name="Rgb">Linear RGB relative to the 18 % grey reference under the same light (above 1 at bright reflections).</param>
/// <param name="Color">The same as Lab (L can exceed 100 at reflections).</param>
/// <param name="LevelPercent">LED brightness the reading came from (the brightest one not overexposed).</param>
/// <param name="Confidence">0..1: pose fit, evenness of the cell, reference light.</param>
public sealed record AnglePoint(
    double TurnDeg, double TiltDeg, int Led,
    double ViewDeg, double LightDeg, double HalfDeg, double LightAzimuthDeg, double ViewAzimuthDeg,
    double[] Rgb, Lab Color, double LevelPercent, double Confidence);

/// <summary>
/// Everything one capture run measured for one cell: its colour over the angles the box
/// covered. Kept beside the cell's ordinary samples, never instead of them.
/// </summary>
public sealed record AngleSet
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>The capture package's session id, and where the package was when imported.</summary>
    public Guid SessionId { get; init; }
    public string Package { get; init; } = "";
    public required string MaterialId { get; init; }
    public required string SourceId { get; init; }
    public required string LensId { get; init; }
    public Guid? GridId { get; init; }
    public string CellLabel { get; init; } = "";
    public required MopaSettings Settings { get; init; }
    /// <summary>Made by the simulated rig: only ever allowed in the demo atlas.</summary>
    public bool Simulated { get; init; }
    public DateTimeOffset CapturedUtc { get; init; }
    public DateTimeOffset ImportedUtc { get; init; }
    /// <summary>The package's overall quality result ("ok", "warn", "fail").</summary>
    public string Quality { get; init; } = "";
    public List<AnglePoint> Points { get; init; } = [];
    public string Notes { get; init; } = "";
}
