using Phi.Agent;
using Phi.Prompts;
using Phi.Providers;

namespace Phi;

/// <summary>
/// Cross-session environment: the resources that are shared across every
/// session a single app instance creates — provider resolver (so a fresh
/// session can rebuild a live provider from a stored name), system-prompt
/// options, tool registry, and the compaction knobs. Built once by the
/// composition root (one of the <c>Program.cs</c> entry points) via
/// <see cref="Default"/> and handed to every <see cref="Session"/> it
/// creates.
/// <para>
/// Composition-root territory: the record is <c>public</c> only so the
/// composition root can build it. UIs never see this type — they hold an
/// <see cref="ISession"/>.
/// </para>
/// </summary>
public sealed record SessionEnvironment
{
    public required IProviderResolver ProviderResolver { get; init; }
    public required SystemPromptOptions SystemPrompt { get; init; }
    public required int? MaxTurns { get; init; }
    public required int ContextWindowTokens { get; init; }
    public required int? AutoCompactTokenThreshold { get; init; }
    public required bool AutoCompactEnabled { get; init; }
    public required int CompactionKeepRecentTokens { get; init; }

    /// <summary>
    /// Optional custom tool set. When null/empty, the built-in
    /// <see cref="BuiltInToolProvider"/> supplies the default toolset for
    /// the session's cwd.
    /// </summary>
    public required IReadOnlyList<Tool>? Tools { get; init; }

    /// <summary>
    /// Builds a <see cref="SessionEnvironment"/> with all compaction knobs
    /// at their defaults and no custom toolset. The composition root
    /// supplies an <see cref="IProviderResolver"/> (typically the
    /// app's <c>ProviderManager</c>) and the system-prompt options; the
    /// <see cref="ContextWindow.DefaultContextWindowTokens"/> /
    /// <see cref="ContextWindow.DefaultCompactionKeepRecentTokens"/>
    /// defaults are filled in here so callers don't have to repeat them.
    /// </summary>
    public static SessionEnvironment Default(
        IProviderResolver providerResolver,
        SystemPromptOptions? systemPrompt = null,
        int? maxTurns = null) =>
        new()
        {
            ProviderResolver = providerResolver,
            SystemPrompt = systemPrompt ?? new SystemPromptOptions(),
            MaxTurns = maxTurns,
            ContextWindowTokens = ContextWindow.DefaultContextWindowTokens,
            AutoCompactTokenThreshold = null,
            AutoCompactEnabled = true,
            CompactionKeepRecentTokens = ContextWindow.DefaultCompactionKeepRecentTokens,
            Tools = null,
        };
}
