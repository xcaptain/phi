using Phi.Agent;
using Phi.Prompts;

namespace Phi;

/// <summary>
/// Configuration for creating a full <see cref="Session"/> with
/// runtime. The provider is injected ready-made — session construction
/// never touches HTTP or concrete provider types; wiring those up is the
/// composition root's job (Program.cs). Passed to
/// <see cref="Sessions.SessionFactory.Create"/> or
/// <see cref="Sessions.SessionFactory.Resume"/>.
/// </summary>
public sealed record SessionConfig
{
    /// <summary>Working directory for this session.</summary>
    public string Cwd { get; init; } = Environment.CurrentDirectory;

    /// <summary>
    /// LLM provider. When set, the factory uses this live instance (the
    /// caller has already resolved an HTTP transport, API key, etc.). When
    /// null, the factory falls back to <see cref="Providers.IProviderResolver"/>
    /// — useful for the resume path where the live provider must be
    /// reconstructed from the session record's provider name rather than
    /// inherited from startup defaults.
    /// </summary>
    public IPhiProvider? Provider { get; init; }

    /// <summary>Model name (e.g. <c>"deepseek-v4-flash"</c>).</summary>
    public string Model { get; init; } = "";

    /// <summary>Provider display name (e.g. <c>"deepseek"</c>).</summary>
    public string ProviderName { get; init; } = "";

    /// <summary>
    /// System-prompt options for the agent. When
    /// <see cref="SystemPromptOptions.ResolvedSystemPrompt"/> is null, the
    /// <see cref="SystemPromptBuilder"/> assembles the final string from
    /// these options, the active tool contributions, project-context files
    /// and skills.
    /// </summary>
    public SystemPromptOptions SystemPrompt { get; init; } = new();

    /// <summary>
    /// Optional cap on the number of turns before the agent stops. Null
    /// (default) means unlimited — the loop runs until the model emits a
    /// message with no tool calls, matching pi's autonomous behavior. Set a
    /// value to act as a safety valve.
    /// </summary>
    public int? MaxTurns { get; init; }

    /// <summary>Tools available to the agent. Defaults to <see cref="BuiltInTools.CreateDefault(string)"/>.</summary>
    public IReadOnlyList<Tool>? Tools { get; init; }

    /// <summary>
    /// Total context window in tokens. Used to derive the auto-compact
    /// threshold when <see cref="AutoCompactTokenThreshold"/> is null.
    /// Defaults to <see cref="ContextWindow.DefaultContextWindowTokens"/>
    /// (128k) — fine for most providers we currently target.
    /// </summary>
    public int ContextWindowTokens { get; init; } = ContextWindow.DefaultContextWindowTokens;

    /// <summary>
    /// Explicit auto-compact threshold in tokens. When set, overrides the
    /// window-minus-reserve derivation. When null, the threshold is
    /// <c>ContextWindowTokens - DefaultCompactionReserveTokens</c>.
    /// </summary>
    public int? AutoCompactTokenThreshold { get; init; }

    /// <summary>
    /// Toggles automatic context compaction. When false the session never
    /// auto-compacts (overflow-compaction still fires as a last resort).
    /// </summary>
    public bool AutoCompactEnabled { get; init; } = true;

    /// <summary>
    /// Tokens of recent messages to keep verbatim when compacting. Must be
    /// smaller than <see cref="ContextWindowTokens"/> -
    /// <see cref="ContextWindow.DefaultCompactionReserveTokens"/>.
    /// </summary>
    public int CompactionKeepRecentTokens { get; init; } =
        ContextWindow.DefaultCompactionKeepRecentTokens;
}
