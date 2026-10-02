using System.Reflection;

namespace MOPAChromaAtlas;

/// <summary>Version and build date, read from assembly metadata stamped by the build.</summary>
internal static class BuildInfo
{
    private static readonly Assembly Asm = typeof(BuildInfo).Assembly;

    /// <summary>"0.1.0": the &lt;Version&gt; in Directory.Build.props, without the commit suffix.</summary>
    public static string Version =>
        Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Asm.GetName().Version?.ToString(3) ?? "?";

    public static string BuildDate =>
        Asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value ?? "";

    /// <summary>
    /// A published single-file release (the only kind that can update itself in place). A copy
    /// built from source runs from a folder of DLLs, where the assembly has a location on disk.
    /// </summary>
    public static bool SingleFile =>
#pragma warning disable IL3000 // Location is empty in a single-file app; that is exactly what is being tested.
        string.IsNullOrEmpty(Asm.Location);
#pragma warning restore IL3000
}
