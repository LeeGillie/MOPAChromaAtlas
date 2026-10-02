using MOPAChroma.Color;

namespace MOPAChroma.Tests;

public class ColorTests
{
    // Test pairs and expected ΔE00 from Sharma, Wu & Dalal (2005), Table 1.
    [Theory]
    [InlineData(50.0000, 2.6772, -79.7751, 50.0000, 0.0000, -82.7485, 2.0425)]
    [InlineData(50.0000, 3.1571, -77.2803, 50.0000, 0.0000, -82.7485, 2.8615)]
    [InlineData(50.0000, 2.8361, -74.0200, 50.0000, 0.0000, -82.7485, 3.4412)]
    [InlineData(50.0000, 0.0000, 0.0000, 50.0000, -1.0000, 2.0000, 2.3669)]
    [InlineData(50.0000, 2.5000, 0.0000, 73.0000, 25.0000, -18.0000, 27.1492)]
    [InlineData(50.0000, 2.5000, 0.0000, 61.0000, -5.0000, 29.0000, 22.8977)]
    [InlineData(50.0000, 2.5000, 0.0000, 56.0000, -27.0000, -3.0000, 31.9030)]
    [InlineData(50.0000, 2.5000, 0.0000, 58.0000, 24.0000, 15.0000, 19.4535)]
    public void Ciede2000_matches_Sharma_reference_pairs(
        double l1, double a1, double b1, double l2, double a2, double b2, double expected)
    {
        var x = new Lab(l1, a1, b1);
        var y = new Lab(l2, a2, b2);
        Assert.Equal(expected, DeltaE.Cie2000(x, y), 4);
        Assert.Equal(expected, DeltaE.Cie2000(y, x), 4); // symmetric
    }

    [Fact]
    public void Identical_colours_have_zero_difference() =>
        Assert.Equal(0, DeltaE.Cie2000(new Lab(42, -7.3, -28.6), new Lab(42, -7.3, -28.6)), 10);

    [Fact]
    public void White_and_black_map_to_the_ends_of_L()
    {
        var white = ColorConvert.ToLab(new Srgb8(255, 255, 255));
        var black = ColorConvert.ToLab(new Srgb8(0, 0, 0));
        Assert.Equal(100, white.L, 2);
        Assert.Equal(0, white.A, 2);
        Assert.Equal(0, white.B, 2);
        Assert.Equal(0, black.L, 6);
    }

    [Fact]
    public void Srgb_round_trips_through_Lab()
    {
        var rng = new Random(1234);
        for (int i = 0; i < 2000; i++)
        {
            var c = new Srgb8((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
            var back = ColorConvert.ToSrgb(ColorConvert.ToLab(c), out bool inGamut);
            Assert.True(inGamut);
            Assert.Equal(c, back);
        }
    }

    [Fact]
    public void Out_of_gamut_Lab_is_reported()
    {
        // A very saturated cyan-blue of the kind oxide films produce on titanium.
        ColorConvert.ToSrgb(new Lab(60, -60, -80), out bool inGamut);
        Assert.False(inGamut);
    }
}
