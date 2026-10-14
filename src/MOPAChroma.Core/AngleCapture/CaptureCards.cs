using MOPAChroma.Atlas;
using MOPAChroma.Capture.Package;
using MOPAChroma.Capture.Simulation;
using MOPAChroma.Grids;
using LumosUltra.Jobs;
using MOPAChroma.Scanning;

namespace MOPAChroma.AngleCapture;

/// <summary>Finding a grid in a capture package, and drawing one for the simulated rig.</summary>
public static class CaptureCards
{
    /// <summary>
    /// The grid on the package's first plain frame (flat, every LED on), found the way Atlas
    /// finds grids in scans: by its squares and its corner crosses. Null when it is not found
    /// with certainty; the user then places it by hand.
    /// </summary>
    public static (CellMap? Map, string Note) FindOnPlainFrame(CapturePackage pkg, TestGrid g)
    {
        var plain = pkg.Frames.FirstOrDefault(f => f.Kind == FrameKind.Plain && f.View is not null);
        if (plain is null) return (null, "The package has no plain frame with a measured view.");
        var frame = pkg.LoadFrame(plain);
        var img = new RgbImage(frame.Width, frame.Height, frame.Pixels);
        var det = GridDetect.Detect(img, g.Layout.Rows, g.Layout.Columns, marks: CardMarks.AtlasCorners(g.Layout));
        if (det is null) return (null, "The grid was not found on the plain frame.");
        if (!det.LooksGood) return (null, $"The grid was found but not with certainty ({det.SquaresFound} squares, fit {det.RmsPixels:0.0} px).");
        if (det.TurnDegrees != 0) return (null, $"The card looks {GridDetect.DescribeTurn(det.TurnDegrees)} in its pocket; put it in the right way round.");
        return (CellMap.FromPlainFrame(det.Grid, g, plain.View!), $"Grid found: {det.SquaresFound} squares, fit {det.RmsPixels:0.0} px.");
    }

    /// <summary>
    /// A simulated card for an atlas grid: its cells where <see cref="CellMap.Centred"/> puts
    /// them, each with an invented oxide thickness that rises with the cell's heat per area
    /// (thin straw films for the gentlest cells, thick blue ones for the hottest).
    /// </summary>
    public static IReadOnlyList<SimCell> SimulatedCells(TestGrid g, SourceProfile source, LensProfile lens)
    {
        var map = CellMap.Centred(g).Cells.ToDictionary(c => c.Label);
        var energy = g.Cells.ToDictionary(c => c.Label, c => Math.Log10(Math.Max(1e-3, Physics.From(c.Settings, source, lens).ArealEnergyJPerMm2)));
        double lo = energy.Values.Min(), hi = energy.Values.Max();
        return g.Cells.Select(c =>
        {
            var p = map[c.Label];
            double t = hi > lo ? (energy[c.Label] - lo) / (hi - lo) : 0.5;
            return new SimCell(c.Label, p.X, p.Y, p.SizeMm, 25 + 190 * t);
        }).ToList();
    }
}
