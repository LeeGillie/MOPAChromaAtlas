using MOPAChroma.Atlas;

namespace MOPAChroma.Scanning;

/// <summary>
/// How a card image was made. It decides how far its colours can be trusted: a flatbed
/// scan lights every card the same way, so scans compare with scans; a photo depends on
/// the room light, white balance and angle, so its colours are only an estimate.
/// </summary>
public enum CaptureKind
{
    /// <summary>A flatbed scanner: fixed light, fixed geometry. Comparable between scans on the same scanner.</summary>
    FlatbedScan,
    /// <summary>A camera, phone or the Lumos's own camera: light and white balance vary. Colours are estimates.</summary>
    Photo,
}

/// <summary>
/// One image of a test card, wherever it came from: a file, the clipboard, a scanner, a
/// USB camera or the laser's camera.
/// </summary>
/// <param name="Image">The pixels.</param>
/// <param name="Kind">Scan or photo: see <see cref="CaptureKind"/>.</param>
/// <param name="Source">Where it came from, in plain words, e.g. "USB camera: Logitech BRIO".</param>
/// <param name="Name">A short name for display and notes: the file name, or a time-stamped name for captures.</param>
/// <param name="FilePath">The file it was read from, if any. Captures are saved to a file before measuring, so this is normally set.</param>
public sealed record CardCapture(RgbImage Image, CaptureKind Kind, string Source, string Name, string? FilePath = null)
{
    /// <summary>
    /// A file's likely kind. Scanners record a resolution (usually 300-1200 dpi); cameras and
    /// phones write 72 or 96, or nothing. The user can always correct it.
    /// </summary>
    public static CaptureKind GuessKind(RgbImage img) => img.Dpi is { } d && d >= 150 ? CaptureKind.FlatbedScan : CaptureKind.Photo;

    /// <summary>The measurement method a sample from this image is recorded with.</summary>
    public MeasureMethod Method => Kind == CaptureKind.FlatbedScan ? MeasureMethod.Scanner : MeasureMethod.Estimated;

    /// <summary>
    /// The viewing condition recorded with each sample. Oxide colours change with the
    /// direction of the light, so the card's turn on the glass is part of it.
    /// </summary>
    public string ViewingCondition(int turnDegrees, bool perspective)
    {
        string turn = turnDegrees == 0 ? "standard orientation (labels readable)" : $"card {GridDetect.DescribeTurn(turnDegrees)}";
        return Kind == CaptureKind.FlatbedScan
            ? $"Flatbed scan, {turn}"
            : $"Photo ({Source}), uncalibrated light and white balance, {turn}" + (perspective ? ", taken at an angle" : "");
    }
}
