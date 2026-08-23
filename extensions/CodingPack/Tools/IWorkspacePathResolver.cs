namespace Phi.Extensions.CodingPack.Tools;

/// <summary>
/// Resolves tool-supplied paths against the session's working directory.
/// Future extensions (<c>--cwd</c> CLI flag, cross-project session picker,
/// embedded project roots) may pass a <see cref="Cwd"/> that differs from
/// the process working directory; the resolver keeps the resolution base
/// independent of <c>Environment.CurrentDirectory</c>.
/// </summary>
public interface IWorkspacePathResolver
{
    /// <summary>Absolute working directory the resolver was created with.</summary>
    string Cwd { get; }

    /// <summary>
    /// Returns an absolute path. Absolute input is normalized as-is; relative
    /// input is joined to <see cref="Cwd"/> before normalization.
    /// </summary>
    string Resolve(string path);
}
