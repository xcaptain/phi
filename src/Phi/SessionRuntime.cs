using Phi.Agent;
using Phi.Prompts;

namespace Phi;

/// <summary>
/// The fully-built runtime a <see cref="Session"/> needs to operate:
/// the harness (with its resolved system prompt and tool set), the injected
/// provider, the available skills, and the cross-session environment that
/// carries the compaction knobs. Built once by the composition pipeline
/// (previously <c>SessionFactory</c>, now an internal path on
/// <see cref="Session"/>) and applied to a fresh or resumed
/// <see cref="Session"/> via <c>Session.ApplyRuntime</c>.
/// </summary>
internal sealed record SessionRuntime
{
    public required Harness Harness { get; init; }

    public required IPhiProvider Provider { get; init; }

    public required string ProviderName { get; init; }

    public required string Model { get; init; }

    public required string SystemPrompt { get; init; }

    public required IReadOnlyList<Tool> Tools { get; init; }

    /// <summary>
    /// Skills available to this session, used by <c>/skill:NAME</c> and
    /// surfaced in the system prompt's <c>&lt;available_skills&gt;</c>.
    /// </summary>
    public required IReadOnlyList<SkillDescriptor> Skills { get; init; }

    /// <summary>
    /// Cross-session environment (compaction knobs, system-prompt options,
    /// tool registry). Carried by reference so <see cref="Session"/> can
    /// read the same knobs for any later <see cref="Session.NewSessionAsync"/>
    /// / <see cref="Session.ResumeAsync"/> call.
    /// </summary>
    public required SessionEnvironment Environment { get; init; }
}
