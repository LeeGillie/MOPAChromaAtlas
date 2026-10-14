namespace MOPAChroma.Scanning;

/// <summary>
/// Where a grid of square cells sits in an image: centre(row, col) = Origin + col·ColStep
/// + row·RowStep. An affine grid, so it copes with the card being shifted, tilted or
/// scanned at any resolution.
///
/// A photo taken at an angle is not affine: squares further from the camera come out
/// smaller. For those, <see cref="Warp"/> maps (column, row) to the image through a full
/// perspective transform and takes precedence; Origin/ColStep/RowStep then describe the
/// grid's centre region, for display (tilt) only. Use the <c>…At</c> members when
/// working near a particular cell.
/// </summary>
public sealed record GridGeometry(PointD Origin, PointD ColStep, PointD RowStep, int Rows, int Columns)
{
    /// <summary>Perspective map from (column, row) to image pixels, or null for an affine grid.</summary>
    public Homography? Warp { get; init; }

    public PointD Centre(int row, int col) => CentreAt(row, col);

    /// <summary>Centre at fractional cell coordinates (row 0.5 is halfway between rows 0 and 1).</summary>
    public PointD CentreAt(double row, double col) =>
        Warp is { } w ? w.Map(col, row) : Origin + ColStep * col + RowStep * row;

    /// <summary>Distance to the next column at this cell, in pixels, as a vector.</summary>
    public PointD ColStepAt(int row, int col) =>
        Warp is null ? ColStep : (CentreAt(row, col + 0.5) - CentreAt(row, col - 0.5));

    /// <summary>Distance to the next row at this cell, in pixels, as a vector.</summary>
    public PointD RowStepAt(int row, int col) =>
        Warp is null ? RowStep : (CentreAt(row + 0.5, col) - CentreAt(row - 0.5, col));

    /// <summary>Mean distance between neighbouring cell centres at this cell, in pixels.</summary>
    public double PitchAt(int row, int col) => (ColStepAt(row, col).Length + RowStepAt(row, col).Length) / 2;

    /// <summary>Mean distance between neighbouring cell centres, in pixels (at the grid's centre).</summary>
    public double Pitch => Warp is null
        ? (ColStep.Length + RowStep.Length) / 2
        : PitchAtFraction((Rows - 1) / 2.0, (Columns - 1) / 2.0);

    /// <summary>Smallest local pitch anywhere on the grid - the safe size for sampling windows.</summary>
    public double MinPitch => Warp is null ? Pitch
        : new[] { (0, 0), (0, Columns - 1), (Rows - 1, 0), (Rows - 1, Columns - 1) }.Min(p => PitchAt(p.Item1, p.Item2));

    /// <summary>Rotation of the rows from horizontal, degrees.</summary>
    public double TiltDegrees => Math.Atan2(ColStep.Y, ColStep.X) * 180 / Math.PI;

    /// <summary>True when the grid carries a perspective term (photographed at an angle).</summary>
    public bool IsPerspective => Warp is { IsAffine: false };

    private double PitchAtFraction(double r, double c) =>
        ((CentreAt(r, c + 0.5) - CentreAt(r, c - 0.5)).Length + (CentreAt(r + 0.5, c) - CentreAt(r - 0.5, c)).Length) / 2;

    /// <summary>
    /// From three clicked cell centres: top-left, top-right, bottom-left cells of the grid.
    /// </summary>
    public static GridGeometry FromCorners(PointD topLeft, PointD topRight, PointD bottomLeft, int rows, int cols)
    {
        if (rows < 2 || cols < 2) throw new ArgumentException("A grid needs at least 2 rows and 2 columns.");
        return new GridGeometry(topLeft, (topRight - topLeft) * (1.0 / (cols - 1)),
                                (bottomLeft - topLeft) * (1.0 / (rows - 1)), rows, cols);
    }

    /// <summary>
    /// A grid placed by a perspective map from (column, row) to image pixels. The affine
    /// members are filled from the map's behaviour at the grid's centre.
    /// </summary>
    public static GridGeometry FromWarp(Homography warp, int rows, int cols)
    {
        if (rows < 1 || cols < 1) throw new ArgumentException("A grid needs at least one row and one column.");
        double rc = (rows - 1) / 2.0, cc = (cols - 1) / 2.0;
        var colStep = warp.Map(cc + 0.5, rc) - warp.Map(cc - 0.5, rc);
        var rowStep = warp.Map(cc, rc + 0.5) - warp.Map(cc, rc - 0.5);
        var origin = warp.Map(cc, rc) - colStep * cc - rowStep * rc;
        return new GridGeometry(origin, colStep, rowStep, rows, cols) { Warp = warp };
    }

    /// <summary>This grid as a (column, row) → pixel map, whether or not it is perspective.</summary>
    public Homography AsWarp() => Warp ?? Homography.Affine(Origin, ColStep, RowStep);

    /// <summary>The same cells in an image scaled by <paramref name="k"/> (e.g. a reduced working copy).</summary>
    public GridGeometry Scaled(double k) =>
        Warp is { } w ? FromWarp(w.ScaleOutput(k), Rows, Columns)
                      : new GridGeometry(Origin * k, ColStep * k, RowStep * k, Rows, Columns);
}
