namespace Phi;

/// <summary>
/// Scans <c>{cwd}/.phi/extensions/</c> for project-level extension
/// assemblies (Sprint 3b foundation). Project extensions are
/// <c>.dll</c> files that implement <c>Phi.Extensions.IPhiExtension</c>
/// and declare a <c>[PhiExtension]</c> attribute — they're loaded
/// alongside the compile-time ones (CodingPack, HelloTool,
/// PermissionGate) but live under the user's git tree, so we gate
/// them through <see cref="Providers.ProjectTrustStore"/> before
/// loading.
/// <para>
/// The directory layout follows the user's mental model: any dll the
/// user drops into <c>.phi/extensions/</c> gets loaded automatically.
/// Hidden subdirectories (<c>.something</c>) are skipped so editors'
/// staging areas (<c>.vs/</c>, <c>.idea/</c>) don't trigger trust
/// prompts. Already-trusted projects skip the prompt via the trust
/// store; declined projects are silently skipped.
/// </para>
/// </summary>
public static class ProjectExtensions
{
    /// <summary>Relative directory under the project root.</summary>
    public const string RelativeDir = ".phi/extensions";

    /// <summary>Absolute directory under <paramref name="cwd"/>.</summary>
    public static string DirectoryFor(string cwd) =>
        Path.Combine(Path.GetFullPath(cwd), RelativeDir.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Enumerate every <c>*.dll</c> candidate under
    /// <see cref="DirectoryFor"/>. Doesn't probe whether each dll
    /// actually contains a <c>[PhiExtension]</c> — that's
    /// <c>ExtensionLoader</c>'s job. Returns an empty list when the
    /// directory is missing or unreadable; the caller treats "no
    /// project extensions" as the common case.
    /// </summary>
    public static IReadOnlyList<string> DiscoverAssemblyPaths(string cwd)
    {
        var dir = DirectoryFor(cwd);
        if (!Directory.Exists(dir)) return [];

        List<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(p =>
                {
                    // Skip dot-prefixed / dot-suffixed files so
                    // editors' temp / staging artefacts don't trip
                    // trust prompts.
                    var name = Path.GetFileName(p);
                    return !name.StartsWith('.') && !name.EndsWith(".deps.json") && !name.EndsWith(".runtimeconfig.json");
                })
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        return paths;
    }

    /// <summary>
    /// Stable, human-readable key for the project's trust record.
    /// Reuses <see cref="SessionPaths.ProjectKey"/> so a project's
    /// trust decision travels with its session index — the same key
    /// that drives <c>~/.phi/sessions/{key}/</c>.
    /// </summary>
    public static string ProjectKey(string cwd) => SessionPaths.ProjectKey(cwd);
}