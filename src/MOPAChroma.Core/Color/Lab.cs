namespace MOPAChroma.Color;

/// <summary>
/// A CIELAB colour (D65 white point, 2° observer). L* is lightness 0..100; a* is
/// green (-) to red (+); b* is blue (-) to yellow (+).
///
/// Lab is the atlas's native colour space because distances in it track what a person
/// sees, which RGB distances do not. Every "is this a new colour?" decision in the
/// planner is a ΔE00 question asked in this space.
/// </summary>
public readonly record struct Lab(double L, double A, double B)
{
    /// <summary>Chroma: distance from the neutral axis. Near 0 means grey.</summary>
    public double Chroma => Math.Sqrt(A * A + B * B);

    /// <summary>Hue angle in degrees, 0..360.</summary>
    public double HueDegrees
    {
        get
        {
            var h = Math.Atan2(B, A) * 180.0 / Math.PI;
            return h < 0 ? h + 360.0 : h;
        }
    }

    public override string ToString() => $"L*{L:0.0} a*{A:0.0} b*{B:0.0}";
}

/// <summary>An 8-bit sRGB colour, for display and for reading photos and scans.</summary>
public readonly record struct Srgb8(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}
