#pragma warning disable CA1416
using System.IO.Compression;
using System.Security.Cryptography;
using MOPAChromaAtlas;
using MOPAChromaAtlas.Updates;

namespace MOPAChromaAtlas.Tests;

/// <summary>See the .csproj. One shared scratch folder, so the tests in this class run one at a time.</summary>
public class UpdaterTests
{
    private const string P = UpdateService.ProgramName;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "atlas-updater-tests-" + Environment.ProcessId);

    public UpdaterTests()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
        Directory.CreateDirectory(Root);
        UpdateService.CacheFolderOverride = Root;
    }

    private static void Script(string path, string version)
    {
        File.WriteAllText(path, $"#!/bin/sh\necho {P} {version}\n");
        File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
    }

    /// <summary>A release zip laid out the way packaging/make_bundle.py lays it out.</summary>
    private static string MakeZip(string version, string exeVersion, Action<ZipArchive>? extra = null)
    {
        string zip = Path.Combine(Root, $"{P}-{version}-linux-x64.zip");
        File.Delete(zip);
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry($"{P}/{P}");
            e.ExternalAttributes = (0x8000 | 0b111_101_101) << 16;
            using (var w = new StreamWriter(e.Open())) w.Write($"#!/bin/sh\necho {P} {exeVersion}\n");
            var r = z.CreateEntry($"{P}/README.txt");
            r.ExternalAttributes = (0x8000 | 0b110_100_100) << 16;
            using (var w = new StreamWriter(r.Open())) w.Write("new readme " + version);
            extra?.Invoke(z);
        }
        return zip;
    }

    private static string Sha(string f) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))).ToLowerInvariant();

    private static string Feed(string version, string zip, string? digest, bool sums = false)
    {
        string sumsPath = Path.Combine(Root, "SHA256SUMS.txt");
        File.WriteAllText(sumsPath, $"{Sha(zip)}  {Path.GetFileName(zip)}\n");
        string asset = $"{{\"name\":\"{Path.GetFileName(zip)}\",\"browser_download_url\":\"{zip}\",\"size\":{new FileInfo(zip).Length}"
                     + (digest is null ? "" : $",\"digest\":\"{digest}\"") + "}";
        string sumsAsset = sums ? $",{{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"{sumsPath}\"}}" : "";
        return $"{{\"tag_name\":\"v{version}\",\"html_url\":\"https://example/notes\",\"assets\":[{asset}{sumsAsset}]}}";
    }

    private static UpdateService.InstallLayout FreshInstall()
    {
        string dir = Path.Combine(Root, "install", P);
        if (Directory.Exists(Path.Combine(Root, "install"))) Directory.Delete(Path.Combine(Root, "install"), true);
        Directory.CreateDirectory(dir);
        Script(Path.Combine(dir, P), "1.3.0");
        File.WriteAllText(Path.Combine(dir, "README.txt"), "old readme");
        File.WriteAllText(Path.Combine(dir, "my-notes.txt"), "the user's own file");
        return UpdateService.DetectLayout(Path.Combine(dir, P), isMac: false, isWindows: false)!;
    }

    private static async Task<string?> TryInstall(UpdateInfo info, UpdateService.InstallLayout l)
    {
        try { await UpdateService.InstallAsync(info, l); return null; }
        catch (UpdateException ex) { return ex.Message; }
    }

    private static bool Untouched(UpdateService.InstallLayout l) =>
        File.ReadAllText(Path.Combine(l.Folder, "README.txt")) == "old readme"
        && File.ReadAllText(l.ExecutablePath).Contains("1.3.0")
        && Directory.GetFileSystemEntries(l.Folder).Length == 3;

    // ------------------------------------------------------------------ everywhere

    [Fact]
    public void Versions_compare_numerically()
    {
        Assert.True(UpdateService.IsNewer("v1.4.0", "1.3.0"));
        Assert.False(UpdateService.IsNewer("1.3", "1.3.0"));
        Assert.False(UpdateService.IsNewer("1.3.0", "1.3.0"));
        Assert.True(UpdateService.IsNewer("1.10.0", "1.9.9"));
        Assert.False(UpdateService.IsNewer("1.4.0-beta", "1.3.0"));
        Assert.False(UpdateService.IsNewer(null, "1.3.0"));
    }

    [Fact]
    public void Release_layouts_are_recognised_and_renamed_copies_are_not()
    {
        // Paths built with Path.Combine, so they mean the same on Windows (which would read "/a" as "D:\a").
        string folder = Path.Combine(Root, P);
        var mac = UpdateService.DetectLayout(Path.Combine(folder, P + ".app", "Contents", "MacOS", P), true, false);
        Assert.NotNull(mac);
        Assert.Equal(Path.GetFullPath(folder), mac!.Folder);
        Assert.Equal(P + ".app", mac.MainItem);
        Assert.Null(UpdateService.DetectLayout(Path.Combine(Root, "b", P + "-1.3.0-osx-arm64"), true, false));
        Assert.Null(UpdateService.DetectLayout(Path.Combine(Root, "b", P + "-1.3.0-linux-x64"), false, false));
        Assert.NotNull(UpdateService.DetectLayout(Path.Combine(Root, P, P + ".exe"), false, true));
        Assert.Null(UpdateService.DetectLayout(Path.Combine(Root, P, "Atlas.exe"), false, true));
    }

    [Fact]
    public async Task A_failed_check_is_quiet_and_never_throws()
    {
        UpdateService.FeedOverride = Path.Combine(Root, "no-such-feed.json");
        try
        {
            var info = await UpdateService.CheckAsync(force: true);
            Assert.NotNull(info.Error);
            Assert.False(info.Newer);
            File.WriteAllText(UpdateService.FeedOverride, "this is not json");
            info = await UpdateService.CheckAsync(force: true);
            Assert.NotNull(info.Error);
            Assert.False(info.Newer);
        }
        finally { UpdateService.FeedOverride = null; }
    }

    [Fact]
    public void Checksum_listing_is_parsed()
    {
        Assert.Equal(new string('b', 64),
            UpdateService.ChecksumFor(new string('a', 64) + $"  {P}-1.4.0-win-x64.zip\n" + new string('b', 64) + " *x.zip", "x.zip"));
    }

    [Fact]
    public void The_built_in_notes_have_a_section_per_release()
    {
        var md = ReleaseNotes.Markdown;
        Assert.NotEmpty(md);
        var sections = ReleaseNotes.Sections(md);
        Assert.NotEmpty(sections);
        Assert.All(sections, s => Assert.NotNull(UpdateService.ParseVersion(s.Version)));
        // Newest first.
        for (int i = 1; i < sections.Count; i++) Assert.True(UpdateService.IsNewer(sections[i - 1].Version, sections[i].Version));
        Assert.Equal(sections[0].Text, ReleaseNotes.For(md, sections[0].Version));
        Assert.Contains(ReleaseNotes.Parse(sections[0].Text), b => b.Kind == ReleaseNotes.BlockKind.Bullet);
    }

    [Fact]
    public void Notes_split_by_version()
    {
        const string md = "# Release notes\n\n## 1.2.0 — b\n\n- two\n\n## 1.1.0\n\nOne **bold** `code` [link](http://x).\n";
        var s = ReleaseNotes.Sections(md);
        Assert.Equal(["1.2.0", "1.1.0"], s.Select(x => x.Version));
        var spans = ReleaseNotes.Parse(s[1].Text)[1].Spans;
        Assert.Contains(spans, x => x.Bold && x.Text == "bold");
        Assert.Contains(spans, x => x.Code && x.Text == "code");
        Assert.Contains(spans, x => x.Text == "link");
    }

    // ------------------------------------------------------------------ Linux and macOS (shell-script programs)

    [Fact]
    public async Task A_good_release_installs_and_the_users_files_stay()
    {
        if (OperatingSystem.IsWindows()) return;
        var zip = MakeZip("1.4.0", "1.4.0");
        var info = UpdateService.Summarise(Feed("1.4.0", zip, "sha256:" + Sha(zip)), "1.3.0", "linux-x64");
        Assert.True(info.Newer);
        Assert.Equal(zip, info.AssetUrl);
        var l = FreshInstall();
        Assert.Contains("built from source", UpdateService.InstallBlocker(info, l, singleFile: false));
        Assert.Null(UpdateService.InstallBlocker(info, l, singleFile: true));
        var other = UpdateService.Summarise(Feed("1.4.0", zip, null), "1.3.0", "win-arm64");
        Assert.Contains("no download for win-arm64", UpdateService.InstallBlocker(other, l, true));

        Assert.Null(await TryInstall(info, l));
        Assert.Contains("1.4.0", File.ReadAllText(l.ExecutablePath));
        Assert.Equal("new readme 1.4.0", File.ReadAllText(Path.Combine(l.Folder, "README.txt")));
        Assert.True(File.Exists(Path.Combine(l.Folder, "my-notes.txt")));
        Assert.True(((int)File.GetUnixFileMode(l.ExecutablePath) & 0b001_000_000) != 0);
        Assert.DoesNotContain(Directory.GetFileSystemEntries(l.Folder), p => p.Contains(".atlas"));
    }

    [Fact]
    public async Task The_checksum_list_is_used_when_there_is_no_digest()
    {
        if (OperatingSystem.IsWindows()) return;
        var zip = MakeZip("1.4.0", "1.4.0");
        var l = FreshInstall();
        var viaSums = UpdateService.Summarise(Feed("1.4.0", zip, null, sums: true), "1.3.0", "linux-x64");
        Assert.Null(UpdateService.InstallBlocker(viaSums, l, true));
        Assert.Null(await TryInstall(viaSums, l));
        Assert.Contains("1.4.0", File.ReadAllText(l.ExecutablePath));
    }

    [Fact]
    public async Task Tampered_or_wrong_releases_are_refused_and_nothing_changes()
    {
        if (OperatingSystem.IsWindows()) return;
        var zip = MakeZip("1.4.0", "1.4.0");

        var l = FreshInstall();
        var bad = UpdateService.Summarise(Feed("1.4.0", zip, "sha256:" + new string('0', 64)), "1.3.0", "linux-x64");
        Assert.Contains("does not match", await TryInstall(bad, l));
        Assert.True(Untouched(l));

        l = FreshInstall();
        var liar = MakeZip("1.4.0", "1.3.9");
        var lie = UpdateService.Summarise(Feed("1.4.0", liar, "sha256:" + Sha(liar)), "1.3.0", "linux-x64");
        Assert.Contains("reports version", await TryInstall(lie, l));
        Assert.True(Untouched(l));

        l = FreshInstall();
        var evil = MakeZip("1.4.0", "1.4.0", z => { var e = z.CreateEntry($"{P}/../../escape.txt"); using var w = new StreamWriter(e.Open()); w.Write("x"); });
        var ev = UpdateService.Summarise(Feed("1.4.0", evil, "sha256:" + Sha(evil)), "1.3.0", "linux-x64");
        Assert.Contains("unexpected path", await TryInstall(ev, l));
        Assert.True(Untouched(l));
        Assert.False(File.Exists(Path.Combine(Root, "escape.txt")));

        l = FreshInstall();
        var stray = MakeZip("1.4.0", "1.4.0", z => z.CreateEntry("Other/file.txt"));
        var st = UpdateService.Summarise(Feed("1.4.0", stray, "sha256:" + Sha(stray)), "1.3.0", "linux-x64");
        Assert.Contains("unexpected file", await TryInstall(st, l));
        Assert.True(Untouched(l));
    }

    [Fact]
    public void Leftovers_are_removed_at_the_next_start()
    {
        if (OperatingSystem.IsWindows()) return;
        var l = FreshInstall();
        File.WriteAllText(Path.Combine(l.Folder, P + ".atlas-old"), "old exe");
        Directory.CreateDirectory(Path.Combine(l.Folder, ".atlas-update"));
        UpdateService.CleanupLeftovers(l);
        Assert.True(Untouched(l));
    }

    [Fact]
    public void A_mac_app_bundle_is_swapped_whole_keeping_files_saved_inside_it()
    {
        if (OperatingSystem.IsWindows()) return;
        string f = Path.Combine(Root, "mac");
        string macos = Path.Combine(f, P + ".app", "Contents", "MacOS");
        Directory.CreateDirectory(macos);
        Script(Path.Combine(macos, P), "1.3.0");
        File.WriteAllText(Path.Combine(macos, "saved.json"), "mine");
        var ml = new UpdateService.InstallLayout(f, P + ".app", $"{P}.app/Contents/MacOS/{P}");
        string mst = Path.Combine(Root, "mac-staging");
        Directory.CreateDirectory(Path.Combine(mst, P + ".app", "Contents", "MacOS"));
        Script(Path.Combine(mst, P + ".app", "Contents", "MacOS", P), "1.4.0");
        UpdateService.Swap(mst, ml, [P + ".app"]);
        Assert.Contains("1.4.0", File.ReadAllText(ml.ExecutablePath));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(macos, "saved.json")));
        Assert.False(Directory.Exists(Path.Combine(f, P + ".app.atlas-old")));
    }
}
