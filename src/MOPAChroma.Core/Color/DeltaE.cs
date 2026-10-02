namespace MOPAChroma.Color;

/// <summary>
/// Colour difference. CIEDE2000 (ΔE00) is the metric the atlas uses everywhere;
/// ΔE76 is kept only for comparison with older published tables.
///
/// Rough reading of ΔE00: under 1 is invisible side by side, 1–2 needs a careful
/// look, 2–5 is clearly a different swatch, over 5 is a different colour. The planner
/// treats two cells closer than <see cref="JustNoticeable"/> as "the same colour".
/// </summary>
public static class DeltaE
{
    /// <summary>Default "same colour" threshold, ΔE00.</summary>
    public const double JustNoticeable = 2.0;

    public static double Cie76(Lab x, Lab y)
    {
        double dl = x.L - y.L, da = x.A - y.A, db = x.B - y.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>
    /// CIEDE2000 with kL = kC = kH = 1, implemented from Sharma, Wu and Dalal (2005),
    /// "The CIEDE2000 Color-Difference Formula: Implementation Notes...". The unit
    /// tests check it against that paper's published test pairs.
    /// </summary>
    public static double Cie2000(Lab x, Lab y)
    {
        const double deg = Math.PI / 180.0;
        const double pow25_7 = 6103515625.0; // 25^7

        double c1 = Math.Sqrt(x.A * x.A + x.B * x.B);
        double c2 = Math.Sqrt(y.A * y.A + y.B * y.B);
        double cBar = (c1 + c2) / 2.0;
        double cBar7 = Math.Pow(cBar, 7);
        double g = 0.5 * (1.0 - Math.Sqrt(cBar7 / (cBar7 + pow25_7)));

        double a1p = (1.0 + g) * x.A, a2p = (1.0 + g) * y.A;
        double c1p = Math.Sqrt(a1p * a1p + x.B * x.B);
        double c2p = Math.Sqrt(a2p * a2p + y.B * y.B);

        double h1p = HueDeg(x.B, a1p);
        double h2p = HueDeg(y.B, a2p);

        double dLp = y.L - x.L;
        double dCp = c2p - c1p;

        double dhp;
        if (c1p * c2p == 0) dhp = 0;
        else
        {
            dhp = h2p - h1p;
            if (dhp > 180) dhp -= 360;
            else if (dhp < -180) dhp += 360;
        }
        double dHp = 2.0 * Math.Sqrt(c1p * c2p) * Math.Sin(dhp / 2.0 * deg);

        double lBarP = (x.L + y.L) / 2.0;
        double cBarP = (c1p + c2p) / 2.0;

        double hBarP;
        if (c1p * c2p == 0) hBarP = h1p + h2p;
        else if (Math.Abs(h1p - h2p) <= 180) hBarP = (h1p + h2p) / 2.0;
        else if (h1p + h2p < 360) hBarP = (h1p + h2p + 360) / 2.0;
        else hBarP = (h1p + h2p - 360) / 2.0;

        double t = 1.0
                   - 0.17 * Math.Cos((hBarP - 30) * deg)
                   + 0.24 * Math.Cos(2 * hBarP * deg)
                   + 0.32 * Math.Cos((3 * hBarP + 6) * deg)
                   - 0.20 * Math.Cos((4 * hBarP - 63) * deg);

        double dTheta = 30.0 * Math.Exp(-Math.Pow((hBarP - 275) / 25.0, 2));
        double cBarP7 = Math.Pow(cBarP, 7);
        double rc = 2.0 * Math.Sqrt(cBarP7 / (cBarP7 + pow25_7));
        double lm50 = (lBarP - 50) * (lBarP - 50);
        double sl = 1.0 + 0.015 * lm50 / Math.Sqrt(20 + lm50);
        double sc = 1.0 + 0.045 * cBarP;
        double sh = 1.0 + 0.015 * cBarP * t;
        double rt = -Math.Sin(2 * dTheta * deg) * rc;

        double tl = dLp / sl, tc = dCp / sc, th = dHp / sh;
        return Math.Sqrt(tl * tl + tc * tc + th * th + rt * tc * th);
    }

    private static double HueDeg(double b, double ap)
    {
        if (b == 0 && ap == 0) return 0;
        double h = Math.Atan2(b, ap) * 180.0 / Math.PI;
        return h < 0 ? h + 360 : h;
    }
}
