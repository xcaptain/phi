using Phi.Agent;

namespace Phi;

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

    /// <summary>
    /// Last error message from a failed run, or null when no error is
    /// active. Set when a run fails; cleared when the next run starts
    /// (so the status bar can restore its normal display and a fresh
    /// failure of the same kind leaves a new record). Between those two
    /// points multiple <see cref="ISession.StateChanged"/> events will
    /// all carry the same value — UI layers that surface errors should
    /// dedup on message equality to avoid spamming the transcript.
    /// </summary>
    public string? LastError { get; init; }
    public int SteeringCount { get; init; }
    public int FollowUpCount { get; init; }
    public string SessionId { get; init; } = "";
    public string Model { get; init; } = "";

    /// <summary>Active provider name (e.g. <c>"deepseek"</c>).</summary>
    public string ProviderName { get; init; } = "";
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
    /// live value is recomputed by <see cref="Session"/> lazily.
    /// </summary>
    public int ContextUsedTokens { get; init; }

    /// <summary>
    /// Auto-compact threshold for this session. Null when auto-compact is
    /// disabled or the context window is unknown. The status bar surfaces
    /// this so users can see when the next compaction will fire.
    /// </summary>
    public int? AutoCompactThreshold { get; init; }

    /// <summary>
    /// Current resolved system prompt (includes built-in tool snippets +
    /// any extension-added guidelines). Updated live by
    /// <c>Session.AddExtensionPromptGuideline</c>. The model only sees
    /// the value at harness-build time; mid-session updates land in the
    /// UI display but not in the model's context until the next prompt
    /// rebuild (Sprint 2).
    /// </summary>
    public string SystemPrompt { get; init; } = "";

    public static readonly SessionState Empty = new();
}
