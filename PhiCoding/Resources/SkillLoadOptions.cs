namespace PhiCoding.Resources;

/// <summary>
/// Inputs for one <see cref="SkillLoader"/> invocation. Phase 5 only
/// needs the session cwd; the home directory for the user-level skills
/// defaults to <c>~/.agents/skills</c> but is overridable for tests.
/// </summary>
public sealed record SkillLoadOptions
{
    public required string Cwd { get; init; }

    /// <summary>
    /// Override the user-level home directory. When null, defaults to
    /// <c>Environment.GetFolderPath(SpecialFolder.UserProfile)</c>. Tests
    /// pass a temp directory here to avoid touching the real home.
    /// </summary>
    public string? HomeDir { get; init; }
}
