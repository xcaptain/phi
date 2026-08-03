namespace PhiCoding.Prompts;

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
}
