namespace MOPAChroma.Scanning;

/// <summary>
/// Watches the folder a scanner saves into and reports each new image once the scanner has
/// finished writing it. Every scanner's own software can save to a folder, on every
/// platform, so this works with any scanner - including ones with no driver interface
/// Atlas could drive directly.
///
/// A file counts as finished when its size has stopped changing for <see cref="SettleTime"/>
/// and it can be opened for reading: scanners often write a large TIFF in several bursts.
/// </summary>
public sealed class ScanFolderWatcher : IDisposable
{
    public static readonly string[] ImageExtensions = [".png", ".tif", ".tiff", ".jpg", ".jpeg", ".bmp"];

    private readonly FileSystemWatcher _fsw;
    private readonly Dictionary<string, (long size, DateTime seen)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private readonly object _gate = new();

    public string Folder { get; }
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Raised (on a thread-pool thread) with the full path of each finished new image.</summary>
    public event Action<string>? ImageArrived;

    public ScanFolderWatcher(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"No folder at {folder}.");
        Folder = folder;
        // Files already there are old scans: never report them.
        foreach (var f in Directory.EnumerateFiles(folder)) _reported.Add(f);

        _fsw = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
        };
        _fsw.Created += (_, e) => Touch(e.FullPath);
        _fsw.Changed += (_, e) => Touch(e.FullPath);
        _fsw.Renamed += (_, e) => Touch(e.FullPath);
        _timer = new Timer(_ => Poll(), null, 250, 250);
        _fsw.EnableRaisingEvents = true;
    }

    private static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void Touch(string path)
    {
        if (!IsImage(path)) return;
        lock (_gate)
            if (!_reported.Contains(path)) _pending[path] = (-1, DateTime.UtcNow);
    }

    /// <summary>Checks pending files; public so tests can drive it without waiting on the timer.</summary>
    public void Poll()
    {
        List<string> ready = [];
        lock (_gate)
        {
            foreach (var (path, (lastSize, seen)) in _pending.ToList())
            {
                long size;
                try { size = new FileInfo(path).Length; }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (!File.Exists(path)) { _pending.Remove(path); continue; }
                if (size != lastSize || size == 0) { _pending[path] = (size, DateTime.UtcNow); continue; }
                if (DateTime.UtcNow - seen < SettleTime) continue;
                if (!CanRead(path)) continue;
                _pending.Remove(path);
                _reported.Add(path);
                ready.Add(path);
            }
        }
        foreach (var p in ready) ImageArrived?.Invoke(p);
    }

    private static bool CanRead(string path)
    {
        try { using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        _fsw.EnableRaisingEvents = false;
        _fsw.Dispose();
        _timer.Dispose();
    }
}
