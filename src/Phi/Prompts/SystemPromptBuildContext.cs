namespace Phi.Prompts;

/// <summary>
/// Immutable input passed to <see cref="ISystemPromptBuilder.Build"/>.
/// Builders must be pure functions of this context: no file IO, no time-of-day
/// reads, no global state.
/// </summary>
public sealed record SystemPromptBuildContext
{
    public required string Cwd { get; init; }

    public required DateOnly CurrentDate { get; init; }

    public required IReadOnlyList<ToolContribution> Tools { get; init; }

    public required IReadOnlyList<SkillDescriptor> Skills { get; init; }

    public required IReadOnlyList<ProjectContextFile> ContextFiles { get; init; }

    public required SystemPromptOptions Options { get; init; }

    /// <summary>
    /// The shell the <c>bash</c> tool executes, so the prompt can tell the
    /// model which syntax to emit. Defaults to <see cref="ShellKind.Bash"/>;
    /// desktop Windows supplies <see cref="ShellKind.PowerShell"/>.
    /// </summary>
    public ShellKind Shell { get; init; } = ShellKind.Bash;
}
