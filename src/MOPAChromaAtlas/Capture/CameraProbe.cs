using FlashCap;
using MOPAChroma.Scanning;

namespace MOPAChromaAtlas.Capture;

/// <summary>
/// Lists every camera FlashCap finds and every format each offers, decodable or not, one
/// camera interface at a time with a time limit each (an interface that hangs, as some do
/// over Remote Desktop, is reported rather than freezing the list). For diagnosing
/// "my camera is not listed".
/// </summary>
public static class CameraProbe
{
    public static void Write(string path)
    {
        void Log(string s) => File.AppendAllText(path, s + Environment.NewLine);
        File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Environment.MachineName}  {Environment.OSVersion}  remote session: {CameraSources.IsRemoteSession}{Environment.NewLine}");
        foreach (var (name, make) in CameraSources.Interfaces())
        {
            Log($"-- {name}");
            var t = Task.Run(() => make().EnumerateDescriptors().ToArray());
            if (!t.Wait(TimeSpan.FromSeconds(15))) { Log("   did not answer within 15 s"); continue; }
            if (t.IsFaulted) { Log("   failed: " + t.Exception!.GetBaseException().Message); continue; }
            foreach (var d in t.Result)
            {
                Log($"   {d.Name}  [{d.Description}]  {d.Characteristics.Length} formats");
                foreach (var c in d.Characteristics)
                    Log($"      {c.Width} x {c.Height}  {c.PixelFormat}  {c.FramesPerSecond}  raw {c.RawPixelFormat}");
            }
        }
        Log("done");
    }

    /// <summary>
    /// Opens every camera whose name contains <paramref name="namePart"/>, through each
    /// interface, in each distinct pixel format at its largest size; reports how many frames
    /// arrive in a few seconds and whether they decode, and saves the first one as a PNG.
    /// </summary>
    public static void Grab(string namePart, string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "grab.txt");
        void Log(string s) => File.AppendAllText(path, s + Environment.NewLine);
        File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Environment.MachineName}  remote session: {CameraSources.IsRemoteSession}{Environment.NewLine}");
        int n = 0;
        foreach (var (iface, make) in CameraSources.Interfaces())
        {
            var list = Task.Run(() => make().EnumerateDescriptors().ToArray());
            if (!list.Wait(TimeSpan.FromSeconds(15)) || list.IsFaulted) { Log($"-- {iface}: could not list"); continue; }
            foreach (var d in list.Result.Where(d => d.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase)))
                foreach (var c in d.Characteristics.Where(c => c.PixelFormat != PixelFormats.Unknown)
                                                   .GroupBy(c => c.PixelFormat).Select(g => g.OrderByDescending(c => c.Width * c.Height).First()))
                {
                    n++;
                    Log($"-- {iface}: {d.Name}  {c.Width} x {c.Height} {c.PixelFormat} {c.FramesPerSecond}");
                    try
                    {
                        var frames = new List<byte[]>();
                        var dev = d.OpenAsync(c, scope => { lock (frames) if (frames.Count < 50) frames.Add(scope.Buffer.CopyImage()); })
                                   .WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                        dev.StartAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                        Thread.Sleep(4000);
                        dev.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                        dev.Dispose();
                        byte[][] got; lock (frames) got = frames.ToArray();
                        Log($"   {got.Length} frames in 4 s; first {(got.Length > 0 ? got[0].Length : 0)} bytes, starts {(got.Length > 0 ? BitConverter.ToString(got[0], 0, Math.Min(4, got[0].Length)) : "-")}");
                        if (got.Length == 0) continue;
                        var last = got[^1];
                        var img = RgbImage.Decode(last);
                        long sum = 0; foreach (var b in img.Pixels) sum += b;
                        Log($"   decoded {img.Width} x {img.Height}, mean level {sum / (double)img.Pixels.Length:0.0}");
                        img.SavePng(Path.Combine(folder, $"grab{n}-{iface.Replace(" ", "")}-{c.PixelFormat}.png"));
                    }
                    catch (Exception ex) { Log("   failed: " + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message); }
                }
        }
        Log(n == 0 ? $"no camera named like '{namePart}'" : "done");
    }

    /// <summary>
    /// Asks the laser's read-only status endpoints that have no documented shape yet (light,
    /// camera calibration, peripherals) and writes the raw answers to a file. Only GETs of
    /// "status"-style endpoints: nothing named like an action (focus_light, logo_light,
    /// laser_led_switch, set_*) is touched, since even a GET there might switch something.
    /// </summary>
    public static void Lumos(string address, string path)
    {
        string[] endpoints =
        [
            "/device/light/status", "/device/light/get_init_value", "/device/light/progress",
            "/camera/get_calibration", "/device/global/cfg_peripheral_status",
        ];
        void Log(string s) => File.AppendAllText(path, s + Environment.NewLine);
        File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Environment.MachineName}  laser {address}{Environment.NewLine}");
        using var client = new LumosUltra.Client.LumosUltraClient(address);
        foreach (var e in endpoints)
        {
            try
            {
                var t = client.GetRawAsync(e);
                if (!t.Wait(TimeSpan.FromSeconds(10))) { Log($"-- {e}: no answer in 10 s"); continue; }
                var body = t.Result;
                Log($"-- {e}: {body.Length} chars");
                Log(body.Length > 20000 ? body[..20000] + " …" : body);
            }
            catch (Exception ex) { Log($"-- {e}: {ex.GetBaseException().Message}"); }
        }
        Log("done");
    }
}
