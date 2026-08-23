namespace Phi.Extensions.CodingPack.Tools;

/// <summary>
/// Default <see cref="IWorkspacePathResolver"/>. Stores an absolute form of
/// <see cref="Cwd"/> so relative-path resolution stays stable even if the
/// process later changes its working directory.
/// </summary>
public sealed class WorkspacePathResolver : IWorkspacePathResolver
{
    public string Cwd { get; }

    public WorkspacePathResolver(string cwd)
    {
        ArgumentException.ThrowIfNullOrEmpty(cwd);
        Cwd = Path.GetFullPath(cwd).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path))
            return Cwd;
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(Cwd, path));
    }
}
