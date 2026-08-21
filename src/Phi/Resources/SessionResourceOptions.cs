namespace Phi.Resources;

/// <summary>
/// Inputs for one <see cref="ProjectContextLoader"/> invocation. Phase 3
/// only needs the session cwd; later phases add knobs for skills roots
/// and project trust.
/// </summary>
public sealed record SessionResourceOptions
{
    public required string Cwd { get; init; }
}
