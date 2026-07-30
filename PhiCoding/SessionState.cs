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
    public Usage Usage { get; init; } = new();
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

    public static readonly SessionState Empty = new();
}
