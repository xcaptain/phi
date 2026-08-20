namespace Phi.Resources;

/// <summary>
/// Walks up from a session's <c>cwd</c> until it finds a directory that
/// looks like a project root, then returns that directory. Returns null
/// when no marker is found before the filesystem root — callers should
/// treat that as "no project root, use cwd as the only scan level".
/// <para>
/// Markers are checked in two tiers: a fixed-name list (<c>.git</c>,
/// <c>global.json</c>, <c>Directory.Build.props</c>,
/// <c>Directory.Packages.props</c>) and a glob list (<c>*.sln</c>,
/// <c>*.slnx</c>, <c>*.csproj</c>, <c>package.json</c>,
/// <c>pyproject.toml</c>, <c>Cargo.toml</c>, <c>go.mod</c>). The first
/// directory containing any marker wins. This favours <c>.git</c> for
/// monorepos (the closest <c>.csproj</c> would otherwise cap discovery
/// to a sub-package).
/// </para>
/// </summary>
public static class ProjectRootLocator
{
    private static readonly string[] GlobMarkers =
    [
        "*.sln",
        "*.slnx",
        "*.csproj",
        "package.json",
        "pyproject.toml",
        "uv.lock",
        "Cargo.toml",
        "go.mod",
    ];

    public static string? Locate(string cwd)
    {
        ArgumentException.ThrowIfNullOrEmpty(cwd);
        var dir = Path.GetFullPath(cwd);

        // First pass: prefer .git. In a monorepo, the closer .csproj of a
        // sub-package must not mask the repository root.
        var found = WalkUp(dir, d => Directory.Exists(Path.Combine(d, ".git")));
        if (found is not null)
            return found;

        // Second pass: any other marker. Closest wins, since once a
        // project-specific marker is found, walking higher usually means a
        // different (less relevant) project.
        return WalkUp(dir, HasAnyMarker);
    }

    private static string? WalkUp(string start, Func<string, bool> predicate)
    {
        var dir = start;
        while (true)
        {
            if (predicate(dir))
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir)
                return null;
            dir = parent;
        }
    }

    private static bool HasAnyMarker(string dir)
    {
        if (File.Exists(Path.Combine(dir, "global.json")))
            return true;
        if (File.Exists(Path.Combine(dir, "Directory.Build.props"))
            || File.Exists(Path.Combine(dir, "Directory.Packages.props")))
            return true;
        foreach (var pattern in GlobMarkers)
        {
            foreach (var _ in Directory.EnumerateFiles(dir, pattern))
                return true;
        }
        return false;
    }
}
