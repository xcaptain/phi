using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Configuration for creating a full <see cref="CodingSession"/> with
/// runtime. The provider is injected ready-made — session construction
/// never touches HTTP or concrete provider types; wiring those up is the
/// composition root's job (Program.cs). Passed to
/// <see cref="CodingSession.Create(SessionConfig)"/> or
/// <see cref="CodingSession.Resume(SessionConfig, string)"/>.
/// </summary>
public sealed record SessionConfig
{
    /// <summary>Working directory for this session.</summary>
    public string Cwd { get; init; } = Environment.CurrentDirectory;

    /// <summary>LLM provider, constructed by the caller.</summary>
    public required IPhiProvider Provider { get; init; }

    /// <summary>Model name (e.g. <c>"deepseek-v4-flash"</c>).</summary>
    public string Model { get; init; } = "";

    /// <summary>System prompt for the agent.</summary>
    public string SystemPrompt { get; init; } = "";

    /// <summary>Max turns before the agent stops.</summary>
    public int MaxTurns { get; init; } = 50;

    /// <summary>Tools available to the agent. Defaults to <see cref="BuiltInTools.CreateDefault"/>.</summary>
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
