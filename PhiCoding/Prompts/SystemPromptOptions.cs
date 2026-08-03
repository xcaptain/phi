namespace PhiCoding.Prompts;

/// <summary>
/// Configuration that controls how <see cref="SystemPromptBuilder"/> renders
/// the final prompt. All three fields are nullable and <c>null</c> means
/// "no override" — note that an empty string is a meaningful value
/// (e.g. <c>CustomBasePrompt = ""</c> explicitly suppresses the default
/// identity block).
/// </summary>
public sealed record SystemPromptOptions
{
    /// <summary>
    /// Final, already-rendered system prompt. When set, the builder is
    /// skipped entirely and this string is sent to the provider verbatim.
    /// </summary>
    public string? ResolvedSystemPrompt { get; init; }

    /// <summary>
    /// Replacement for the Phi default identity and guidelines. When null,
    /// the builder uses its default base prompt. An empty string still
    /// counts as a replacement — it suppresses the base while keeping the
    /// project-context, skills, date and cwd sections.
    /// </summary>
    public string? CustomBasePrompt { get; init; }

    /// <summary>
    /// Text appended after the base (default or custom) prompt.
    /// </summary>
    public string? AppendSystemPrompt { get; init; }
}
