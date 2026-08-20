using Phi.Prompts;

namespace Phi.Resources;

/// <summary>
/// Immutable snapshot of every resource a session's prompt builder can
/// consume. Captured once per <c>BuildHarness</c> call so the prompt is
/// stable for the session's lifetime; <c>/reload</c> (Phase 6) refreshes
/// it as a whole.
/// <para>
/// Phase 3 populates <see cref="ContextFiles"/> from AGENTS.md discovery;
/// <see cref="Skills"/> and the prompt-template slot are reserved for
/// later phases and stay empty until then.
/// </para>
/// </summary>
public sealed record SessionResources
{
    public IReadOnlyList<ProjectContextFile> ContextFiles { get; init; } = [];

    public IReadOnlyList<SkillDescriptor> Skills { get; init; } = [];

    public IReadOnlyList<ResourceDiagnostic> Diagnostics { get; init; } = [];
}
