using Phi.Agent;
using Phi.Prompts;

namespace Phi.Sessions;

/// <summary>
/// The fully-built runtime a <see cref="Session"/> needs to operate:
/// the harness (with its resolved system prompt and tool set), the injected
/// provider, the available skills, and the config knobs that feed the
/// state machine.
/// <para>
/// Built once by <see cref="SessionFactory"/> from a
/// <see cref="SessionConfig"/> via the shared resources, tools, prompt,
/// harness pipeline. A session that only needs persistence (no LLM) is
/// created without a <see cref="SessionRuntime"/> and binds one later via
/// <c>Session.ApplyRuntime</c>.
/// </para>
/// </summary>
public sealed record SessionRuntime
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

    public required SessionConfig Config { get; init; }
}
