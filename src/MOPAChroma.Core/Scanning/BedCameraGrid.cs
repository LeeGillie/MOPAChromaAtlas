namespace MOPAChroma.Scanning;

/// <summary>Where an Atlas grid lands in a laser-camera picture (<see cref="LumosUltra.Camera.BedCamera"/>, extended with Atlas's grid layout).</summary>
public static class BedCameraGrid
{
    /// <summary>
    /// Where the squares of a grid engraved with its centre at (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>) on the bed appear in the picture (as GcodeExport lays them out).
    /// </summary>
    public static GridGeometry? GridAt(this BedCamera cam, MOPAChroma.Grids.GridLayout l, double centerX, double centerY)
    {
        if (l.Rows < 2 || l.Columns < 2) return null;
        double pitch = l.CellSizeMm + l.GapMm;
        double x0 = centerX - l.WidthMm / 2 + l.CellSizeMm / 2, y0 = centerY - l.HeightMm / 2 + l.CellSizeMm / 2;
        var src = new List<PointD>();
        var dst = new List<PointD>();
        for (int r = 0; r < l.Rows; r++)
            for (int c = 0; c < l.Columns; c++)
            {
                src.Add(new PointD(c, r));
                dst.Add(cam.ToImage(new PointD(x0 + c * pitch, y0 + r * pitch)));
            }
        return Homography.Fit(src, dst) is { } h ? GridGeometry.FromWarp(h, l.Rows, l.Columns) : null;
    }
}
