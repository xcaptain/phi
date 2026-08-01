using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Immutable snapshot of the session's public state. Each change produces
/// a new instance; the session fires <see cref="ISession.StateChanged"/>
/// so all bound frontends can re-render.
/// </summary>
public sealed record SessionState
{
    public IReadOnlyList<IAgentMessage> Messages { get; init; } = [];
    public bool IsRunning { get; init; }
    public int Turn { get; init; }
    public SessionStats Stats { get; init; } = SessionStats.Zero;
    public string? LastError { get; init; }
    public int SteeringCount { get; init; }
    public int FollowUpCount { get; init; }
    public string SessionId { get; init; } = "";
    public string Model { get; init; } = "";
    public string? SessionTitle { get; init; }

    /// <summary>
    /// Whether this session has been written to disk (index + transcript).
    /// Fresh TUI sessions start unpersisted — an id is allocated eagerly,
    /// but nothing is written until the first message or explicit rename.
    /// </summary>
    public bool IsPersisted { get; init; }

    /// <summary>
    /// Rough token estimate of the current context (system + messages +
    /// tools). Refreshed on resume and at every compaction boundary; the
    /// live value is recomputed by <see cref="CodingSession"/> lazily.
    /// </summary>
    public int ContextUsedTokens { get; init; }

    /// <summary>
    /// Auto-compact threshold for this session. Null when auto-compact is
    /// disabled or the context window is unknown. The status bar surfaces
    /// this so users can see when the next compaction will fire.
    /// </summary>
    public int? AutoCompactThreshold { get; init; }

    public static readonly SessionState Empty = new();
}
