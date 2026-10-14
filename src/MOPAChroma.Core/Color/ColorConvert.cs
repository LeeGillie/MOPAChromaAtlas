namespace MOPAChroma.Color;

/// <summary>
/// sRGB ⇄ CIELAB (D65) conversion. Standard IEC 61966-2-1 transfer curve and the
/// sRGB D65 primaries matrix; no chromatic adaptation, because both ends are D65.
///
/// Caveat recorded here so nobody forgets it: an sRGB value read from a phone photo
/// is only as trustworthy as the white balance and exposure of that photo. Absolute
/// colour needs a reference target in frame (see docs/DESIGN.md, "Measuring colour").
/// </summary>
public static class ColorConvert
{
    // D65 reference white, Y normalised to 1.
    private const double Xn = 0.95047, Yn = 1.00000, Zn = 1.08883;

    private const double Epsilon = 216.0 / 24389.0; // (6/29)^3
    private const double Kappa = 24389.0 / 27.0;    // (29/3)^3

    public static Lab ToLab(Srgb8 c) => ToLab(c.R / 255.0, c.G / 255.0, c.B / 255.0);

    /// <summary>sRGB components in 0..1 to Lab.</summary>
    public static Lab ToLab(double r, double g, double b)
    {
        double rl = ToLinear(r), gl = ToLinear(g), bl = ToLinear(b);

        double x = 0.4124564 * rl + 0.3575761 * gl + 0.1804375 * bl;
        double y = 0.2126729 * rl + 0.7151522 * gl + 0.0721750 * bl;
        double z = 0.0193339 * rl + 0.1191920 * gl + 0.9503041 * bl;

        double fx = F(x / Xn), fy = F(y / Yn), fz = F(z / Zn);
        return new Lab(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
    }

    /// <summary>
    /// Linear-light sRGB (not gamma-encoded) to Lab, without clipping: values above 1, such
    /// as a bright reflection measured against a grey reference, give L* above 100.
    /// </summary>
    public static Lab LinearToLab(double rl, double gl, double bl)
    {
        double x = 0.4124564 * rl + 0.3575761 * gl + 0.1804375 * bl;
        double y = 0.2126729 * rl + 0.7151522 * gl + 0.0721750 * bl;
        double z = 0.0193339 * rl + 0.1191920 * gl + 0.9503041 * bl;
        double fx = F(Math.Max(0, x) / Xn), fy = F(Math.Max(0, y) / Yn), fz = F(Math.Max(0, z) / Zn);
        return new Lab(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
    }

    /// <summary>
    /// Lab to 8-bit sRGB for display. Colours outside the sRGB gamut are clipped per
    /// channel; <paramref name="inGamut"/> reports whether that happened, so the UI can
    /// mark swatches that cannot be shown truthfully on screen (saturated oxide blues
    /// often cannot).
    /// </summary>
    public static Srgb8 ToSrgb(Lab lab, out bool inGamut)
    {
        double fy = (lab.L + 16.0) / 116.0;
        double fx = fy + lab.A / 500.0;
        double fz = fy - lab.B / 200.0;

        double x = Xn * FInv(fx), y = Yn * FInv(fy), z = Zn * FInv(fz);

        double rl = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        double gl = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        double bl = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;

        const double tol = 1e-4;
        inGamut = rl >= -tol && rl <= 1 + tol && gl >= -tol && gl <= 1 + tol && bl >= -tol && bl <= 1 + tol;

        return new Srgb8(To8(FromLinear(rl)), To8(FromLinear(gl)), To8(FromLinear(bl)));
    }

    public static Srgb8 ToSrgb(Lab lab) => ToSrgb(lab, out _);

    private static double ToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double FromLinear(double c)
    {
        c = Math.Clamp(c, 0.0, 1.0);
        return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
    }

    private static byte To8(double c) => (byte)Math.Round(Math.Clamp(c, 0.0, 1.0) * 255.0);

    private static double F(double t) => t > Epsilon ? Math.Cbrt(t) : (Kappa * t + 16.0) / 116.0;

    private static double FInv(double f)
    {
        double f3 = f * f * f;
        return f3 > Epsilon ? f3 : (116.0 * f - 16.0) / Kappa;
    }
}
