using LumosUltra.Jobs;

namespace LumosUltra.Camera;

/// <summary>
/// The job that engraves the camera calibration target (<see cref="BedCameraTarget"/>): nine
/// crosses at known bed positions, each with 1-9 short bars beside it so a picture of them
/// can be matched to the layout whichever way up it is. Drawn as vector strokes with the
/// given settings (only power, speed, frequency and pulse width are used). Engrave it once on
/// any scrap that marks, photograph it with the laser's camera, and click the crosses.
/// </summary>
public static class CalibrationTarget
{
    public static string Gcode(MopaSettings s)
    {
        var job = new MakeItJob();
        job.Pulse(s.FrequencyKHz, s.PulseWidthNs).Feed(s.SpeedMmPerS);
        int power = MakeItJob.PowerS(s.PowerPercent);
        var pts = BedCameraTarget.Points;
        const double arm = BedCameraTarget.ArmMm;
        for (int i = 0; i < pts.Length; i++)
        {
            double cx = pts[i].X, cy = pts[i].Y;
            StrokeX(job, cx - arm, cx + arm, cy, power);
            StrokeY(job, cx, cy - arm, cy + arm, power);
            // i + 1 bars to the right of the cross (to the left on the right-hand column, to stay on the bed).
            double dir = cx > 150 ? -1 : 1;
            for (int b = 0; b <= i; b++)
            {
                double x = cx + dir * (BedCameraTarget.BarGapMm + b * BedCameraTarget.BarSpacingMm);
                double half = BedCameraTarget.BarLengthMm / 2;
                StrokeY(job, x, cy - half, cy + half, power);
            }
        }
        return job.Finish();
    }

    // Axis-aligned strokes in the confirmed pattern, each three lines 0.05 mm apart so it
    // reads clearly on any finish.
    private static void StrokeX(MakeItJob job, double x0, double x1, double y, int power)
    {
        for (int k = -1; k <= 1; k++) job.LineX(x0, x1, y + k * 0.05, power);
    }

    private static void StrokeY(MakeItJob job, double x, double y0, double y1, int power)
    {
        for (int k = -1; k <= 1; k++) job.LineY(x + k * 0.05, y0, y1, power);
    }
}
