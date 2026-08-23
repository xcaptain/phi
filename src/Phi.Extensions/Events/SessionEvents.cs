namespace Phi.Extensions.Events;

// ──────── Session info mutations ────────

/// <summary>
/// Fired when session metadata changes: title (rename), model
/// (<c>SwitchModel</c>), or provider (<c>SwitchProvider</c>). Useful for
/// extensions that mirror session state to external systems (e.g.
/// analytics).
/// </summary>
public sealed record SessionInfoChangedEvent(
    string SessionId,
    string? NewTitle,
    string Model,
    string ProviderName) : PhiEvent;

// ──────── Thinking level (Phi doesn't have this yet — placeholder) ────────

/// <summary>Reserved for future thinking-level control. Phi v1 doesn't emit this.</summary>
public sealed record ThinkingLevelChangedEvent(string Level) : PhiEvent;
