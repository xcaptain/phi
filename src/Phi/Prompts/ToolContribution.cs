using Phi.Agent;

namespace Phi.Prompts;

/// <summary>
/// Wraps an executable <see cref="Tool"/> with prompt-facing metadata. The
/// agent loop only consumes <see cref="Tool"/>; the system-prompt builder
/// consumes <see cref="PromptSnippet"/>, <see cref="PromptGuidelines"/> and
/// <see cref="Capabilities"/>.
/// </summary>
public sealed record ToolContribution
{
    public required Tool Tool { get; init; }

    /// <summary>
    /// Optional one-line description used in the available-tools section.
    /// Falls back to <see cref="Tool.Description"/> when null.
    /// </summary>
    public string? PromptSnippet { get; init; }

    /// <summary>Behavioral rules appended to the guidelines section.</summary>
    public IReadOnlyList<string> PromptGuidelines { get; init; } = [];

    /// <summary>Capability flags for prompt-time gating.</summary>
    public ToolCapabilities Capabilities { get; init; }

    /// <summary>Provenance tag (e.g. <c>"builtin"</c>, <c>"mcp"</c>) for diagnostics.</summary>
    public string Source { get; init; } = "builtin";
}
