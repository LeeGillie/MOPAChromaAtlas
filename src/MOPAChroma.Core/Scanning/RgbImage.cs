using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MOPAChroma.Scanning;

/// <summary>
/// A decoded image as plain 8-bit RGB, row-major. Kept free of any imaging library type
/// so the measuring code (and its tests) work on bytes alone.
/// </summary>
public sealed class RgbImage
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Width × Height × 3 bytes, R G B.</summary>
    public byte[] Pixels { get; }
    /// <summary>Horizontal resolution from the file, if it recorded one.</summary>
    public double? Dpi { get; init; }

    public RgbImage(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height * 3)
            throw new ArgumentException("Pixel buffer does not match the size.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static RgbImage Load(string path)
    {
        using var img = Image.Load<Rgb24>(path);
        return From(img);
    }

    /// <summary>Decodes an encoded image (PNG, JPEG, BMP, TIFF, …) held in memory: a camera frame, a pasted image.</summary>
    public static RgbImage Decode(byte[] encoded)
    {
        using var img = Image.Load<Rgb24>(encoded);
        return From(img);
    }

    /// <summary>Encodes as PNG (lossless), e.g. to keep a copy of a camera capture beside the atlas.</summary>
    public void SavePng(string path)
    {
        using var img = Image.LoadPixelData<Rgb24>(Pixels, Width, Height);
        img.SaveAsPng(path);
    }

    private static RgbImage From(Image<Rgb24> img)
    {
        var buf = new byte[img.Width * img.Height * 3];
        img.CopyPixelDataTo(buf);

        double? dpi = null;
        var m = img.Metadata;
        if (m.HorizontalResolution > 1)
            dpi = m.ResolutionUnits switch
            {
                SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerInch => m.HorizontalResolution,
                SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerCentimeter => m.HorizontalResolution * 2.54,
                SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerMeter => m.HorizontalResolution * 0.0254,
                _ => null,
            };
        return new RgbImage(img.Width, img.Height, buf) { Dpi = dpi };
    }

    /// <summary>Sum of R+G+B at a pixel (0..765); coordinates must be inside the image.</summary>
    public int Luma3(int x, int y)
    {
        int i = (y * Width + x) * 3;
        return Pixels[i] + Pixels[i + 1] + Pixels[i + 2];
    }

    /// <summary>The image turned a quarter turn clockwise (the Lumos camera delivers its pictures on their side).</summary>
    public RgbImage RotateClockwise()
    {
        int w = Width, h = Height;
        var dst = new byte[Pixels.Length];
        // Source (x, y) lands at (h - 1 - y, x) in a w-tall, h-wide result.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 3, d = (x * h + (h - 1 - y)) * 3;
                dst[d] = Pixels[s]; dst[d + 1] = Pixels[s + 1]; dst[d + 2] = Pixels[s + 2];
            }
        return new RgbImage(h, w, dst) { Dpi = Dpi };
    }

    /// <summary>Left-right mirror image.</summary>
    public RgbImage MirrorHorizontal()
    {
        var dst = new byte[Pixels.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int s = (y * Width + x) * 3, d = (y * Width + (Width - 1 - x)) * 3;
                dst[d] = Pixels[s]; dst[d + 1] = Pixels[s + 1]; dst[d + 2] = Pixels[s + 2];
            }
        return new RgbImage(Width, Height, dst) { Dpi = Dpi };
    }

    /// <summary>Turned clockwise by <paramref name="quarterTurns"/> quarter turns (any integer), then mirrored if asked.</summary>
    public RgbImage Oriented(int quarterTurns, bool mirror)
    {
        var img = this;
        for (int i = 0; i < ((quarterTurns % 4) + 4) % 4; i++) img = img.RotateClockwise();
        return mirror ? img.MirrorHorizontal() : img;
    }

    /// <summary>
    /// Per-pixel mean of several frames of the same scene. Averaging N camera frames cuts
    /// sensor noise by about √N, which matters when telling close colours apart.
    /// </summary>
    public static RgbImage Average(IReadOnlyList<RgbImage> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("No frames to average.", nameof(frames));
        int w = frames[0].Width, h = frames[0].Height;
        if (frames.Any(f => f.Width != w || f.Height != h))
            throw new ArgumentException("Frames differ in size (the camera changed resolution mid-capture).", nameof(frames));
        var sum = new int[w * h * 3];
        foreach (var f in frames)
            for (int i = 0; i < sum.Length; i++) sum[i] += f.Pixels[i];
        var dst = new byte[sum.Length];
        int n = frames.Count;
        for (int i = 0; i < sum.Length; i++) dst[i] = (byte)((sum[i] + n / 2) / n);
        return new RgbImage(w, h, dst);
    }
}
