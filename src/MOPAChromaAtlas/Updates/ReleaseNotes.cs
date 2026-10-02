using System.Text;
using System.Text.RegularExpressions;

namespace MOPAChromaAtlas.Updates;

/// <summary>
/// This build's own release notes, and whether it has just replaced an older version.
///
/// The notes are CHANGELOG.md at the repository root, embedded in the program at build time:
/// one "## x.y.z" section per release, newest first. The release workflow puts that version's
/// section at the top of the GitHub release page too, so the program and its page say the same
/// thing, and the notes read offline. The build checks (--whats-new) that the newest section is
/// this version's, so a release cannot ship with the previous release's notes inside it.
///
/// "Just updated" is decided by the version that starts: the last version to run is remembered
/// in preferences, and a start by a newer one offers the notes once. A first-ever run offers
/// nothing, because there is nothing to compare with.
/// </summary>
public static class ReleaseNotes
{
    private const string ResourceName = "MOPAChromaAtlas.CHANGELOG.md";
    public const string TagPagePrefix = "https://github.com/" + UpdateService.Repo + "/releases/tag/v";

    /// <summary>The release page for this version.</summary>
    public static string PageUrl => TagPagePrefix + BuildInfo.Version;

    private static string? _markdown;

    /// <summary>The embedded changelog, or "" when the build did not carry it.</summary>
    public static string Markdown => _markdown ??= Load();

    private static string Load()
    {
        try
        {
            using var s = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(ResourceName);
            if (s is null) return "";
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd();
        }
        catch (Exception) { return ""; }
    }

    /// <summary>Each release's section: version and its Markdown (heading included), newest first.</summary>
    public static List<(string Version, string Text)> Sections(string markdown)
    {
        var list = new List<(string, string)>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        int start = -1;
        string? version = null;
        for (int i = 0; i <= lines.Length; i++)
        {
            Match? m = i < lines.Length ? Regex.Match(lines[i], @"^##\s+v?(\d+(?:\.\d+)*)") : null;
            if (i == lines.Length || m!.Success)
            {
                if (version is not null) list.Add((version, string.Join("\n", lines[start..i]).Trim()));
                if (i < lines.Length) { version = m!.Groups[1].Value; start = i; }
            }
        }
        return list;
    }

    /// <summary>The version the newest section describes, or null.</summary>
    public static string? NewestVersion(string markdown) => Sections(markdown).Select(s => s.Version).FirstOrDefault();

    /// <summary>True when the newest section is this build's version.</summary>
    public static bool DescribesThisBuild(string markdown)
    {
        var v = NewestVersion(markdown);
        return v is not null && !UpdateService.IsNewer(v, BuildInfo.Version) && !UpdateService.IsNewer(BuildInfo.Version, v);
    }

    /// <summary>The section for one version, or null.</summary>
    public static string? For(string markdown, string version) =>
        Sections(markdown).Where(s => !UpdateService.IsNewer(s.Version, version) && !UpdateService.IsNewer(version, s.Version))
                          .Select(s => s.Text).FirstOrDefault();

    // ------------------------------------------------------------------ a small Markdown reader

    public enum BlockKind { Heading, Paragraph, Bullet }
    public sealed record Span(string Text, bool Bold = false, bool Italic = false, bool Code = false);
    public sealed record Block(BlockKind Kind, IReadOnlyList<Span> Spans);

    /// <summary>
    /// Just enough Markdown for release notes: headings, paragraphs, "-" bullets with indented
    /// continuation lines, **bold**, *italic*, `code` and [links](url), whose text is kept and
    /// address dropped. Anything else comes through as plain text, never lost.
    /// </summary>
    public static List<Block> Parse(string markdown)
    {
        var blocks = new List<Block>();
        var para = new StringBuilder();
        BlockKind kind = BlockKind.Paragraph;

        void Flush()
        {
            if (para.Length > 0) blocks.Add(new Block(kind, Inline(para.ToString())));
            para.Clear();
            kind = BlockKind.Paragraph;
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith("<!--")) { Flush(); continue; }
            if (line.StartsWith('#'))
            {
                Flush();
                blocks.Add(new Block(BlockKind.Heading, Inline(line.TrimStart('#').Trim())));
                continue;
            }
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                Flush();
                kind = BlockKind.Bullet;
                para.Append(line[2..].Trim());
                continue;
            }
            if (para.Length > 0) para.Append(' ');
            para.Append(line.Trim());
        }
        Flush();
        return blocks;
    }

    private static readonly Regex InlineToken = new(
        @"\*\*(?<b>.+?)\*\*|(?<![\w*])\*(?<i>[^*\s][^*]*?)\*(?![\w*])|`(?<c>[^`]+)`|\[(?<l>[^\]]+)\]\([^)]*\)",
        RegexOptions.Compiled);

    public static List<Span> Inline(string text)
    {
        var spans = new List<Span>();
        int at = 0;
        foreach (Match m in InlineToken.Matches(text))
        {
            if (m.Index > at) spans.Add(new Span(text[at..m.Index]));
            if (m.Groups["b"].Success) spans.AddRange(Inline(m.Groups["b"].Value).Select(s => s with { Bold = true }));
            else if (m.Groups["i"].Success) spans.Add(new Span(m.Groups["i"].Value, Italic: true));
            else if (m.Groups["c"].Success) spans.Add(new Span(m.Groups["c"].Value, Code: true));
            else spans.AddRange(Inline(m.Groups["l"].Value));
            at = m.Index + m.Length;
        }
        if (at < text.Length) spans.Add(new Span(text[at..]));
        return spans;
    }

    /// <summary>The notes as plain text, for a terminal.</summary>
    public static string PlainText(string markdown)
    {
        var sb = new StringBuilder();
        foreach (var b in Parse(markdown))
        {
            string t = string.Concat(b.Spans.Select(s => s.Text));
            sb.AppendLine(b.Kind == BlockKind.Bullet ? "  - " + t : t);
            if (b.Kind != BlockKind.Bullet) sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    // ------------------------------------------------------------------ just updated?

    /// <summary>Set by <see cref="Probe"/>: this start is the first by a newer version.</summary>
    public static bool JustUpdated { get; private set; }

    /// <summary>The version that ran before, when it is known.</summary>
    public static string? UpdatedFrom { get; private set; }

    /// <summary>Decide, before anything this session writes preferences, whether a newer version is starting.</summary>
    public static void Probe()
    {
        try
        {
            string? last = AppPreferences.Current.LastRunVersion;
            JustUpdated = last is not null && UpdateService.IsNewer(BuildInfo.Version, last);
            UpdatedFrom = last;
        }
        catch (Exception) { JustUpdated = false; }
    }

    /// <summary>Remember this version as the last to run, so the notes are offered once.</summary>
    public static void MarkSeen()
    {
        var p = AppPreferences.Current;
        if (p.LastRunVersion == BuildInfo.Version) return;
        p.LastRunVersion = BuildInfo.Version;
        p.Save();
    }
}
