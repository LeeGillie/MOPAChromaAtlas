using MOPAChroma.Atlas;
using MOPAChroma.Scanning;

namespace MOPAChroma.Tests;

public class CaptureTests
{
    private static RgbImage Gradient(int w, int h, int seed = 0)
    {
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                px[i] = (byte)(x * 10 + seed); px[i + 1] = (byte)(y * 20); px[i + 2] = (byte)(x + y);
            }
        return new RgbImage(w, h, px);
    }

    [Fact]
    public void Rotate_clockwise_moves_the_top_left_corner_to_the_top_right()
    {
        var img = Gradient(4, 3);
        var rot = img.RotateClockwise();
        Assert.Equal(3, rot.Width);
        Assert.Equal(4, rot.Height);
        // Source top-left (0,0) → result top-right (w'-1, 0); source bottom-left (0,h-1) → result (0,0).
        Assert.Equal(img.Pixels[0], rot.Pixels[(0 * 3 + 2) * 3]);
        Assert.Equal(img.Pixels[(2 * 4 + 0) * 3], rot.Pixels[0]);
        // Four quarter turns are the identity.
        Assert.Equal(img.Pixels, rot.RotateClockwise().RotateClockwise().RotateClockwise().Pixels);
    }

    [Fact]
    public void Average_takes_the_per_pixel_mean_and_rejects_mismatched_frames()
    {
        var a = Gradient(5, 4, seed: 0);
        var b = Gradient(5, 4, seed: 2);
        var avg = RgbImage.Average([a, b]);
        Assert.Equal((byte)1, avg.Pixels[0]); // (0 + 2) / 2
        Assert.Throws<ArgumentException>(() => RgbImage.Average([a, Gradient(4, 4)]));
    }

    [Fact]
    public void Png_round_trip_is_lossless()
    {
        var img = Gradient(7, 5);
        var path = Path.Combine(Path.GetTempPath(), $"mopachroma-{Guid.NewGuid():N}.png");
        try
        {
            img.SavePng(path);
            Assert.Equal(img.Pixels, RgbImage.Decode(File.ReadAllBytes(path)).Pixels);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Scans_and_photos_are_recorded_differently()
    {
        var img = new RgbImage(1, 1, new byte[3]) { Dpi = 600 };
        Assert.Equal(CaptureKind.FlatbedScan, CardCapture.GuessKind(img));
        Assert.Equal(CaptureKind.Photo, CardCapture.GuessKind(new RgbImage(1, 1, new byte[3]) { Dpi = 72 }));
        Assert.Equal(CaptureKind.Photo, CardCapture.GuessKind(new RgbImage(1, 1, new byte[3])));

        var scan = new CardCapture(img, CaptureKind.FlatbedScan, "File", "card.png");
        var photo = new CardCapture(img, CaptureKind.Photo, "USB camera: Test cam", "capture.png");
        Assert.Equal(MeasureMethod.Scanner, scan.Method);
        Assert.Equal(MeasureMethod.Estimated, photo.Method);
        Assert.Contains("standard orientation", scan.ViewingCondition(0, false));
        Assert.Contains("anticlockwise", scan.ViewingCondition(270, false));
        Assert.Contains("USB camera", photo.ViewingCondition(0, true));
        Assert.Contains("at an angle", photo.ViewingCondition(0, true));
    }

    [Fact]
    public void Folder_watcher_reports_a_new_scan_once_it_has_finished_writing()
    {
        var dir = Directory.CreateTempSubdirectory("mopachroma-watch-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "old.png"), "already here");
            using var w = new ScanFolderWatcher(dir) { SettleTime = TimeSpan.Zero };
            var got = new List<string>();
            w.ImageArrived += got.Add;

            var path = Path.Combine(dir, "new scan.png");
            File.WriteAllBytes(path, new byte[100]);
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not an image");

            // Size seen, then seen unchanged: reported once.
            for (int i = 0; i < 40 && got.Count == 0; i++) { w.Poll(); Thread.Sleep(50); }
            w.Poll();
            Assert.Equal([path], got);
        }
        finally { Directory.Delete(dir, true); }
    }
}
