namespace PhiCoding.Status;

/// <summary>
/// Side-effect surface for the status bar — implemented by TUI's
/// <c>PhiStatusBar</c> and (eventually) the desktop <c>StatusBarView</c>.
/// The router calls these methods when the session's reactive state changes;
/// implementations render the value however they like.
/// </summary>
public interface ISessionStatusSink
{
    /// <summary>Spinner / "running" indicator. Set from <c>TurnStartEvent</c> and cleared on <c>TurnEndEvent</c>.</summary>
    void SetRunning(bool isRunning);

    /// <summary>
    /// Current turn number, surfaced as <c>"turn N"</c> while running.
    /// Driven by <see cref="SessionState.Turn"/> on every state change.
    /// </summary>
    void SetTurn(int turn);

    /// <summary>Count of queued steering + follow-up messages.</summary>
    void SetQueuedCount(int count);

    /// <summary>Cumulative token usage (input / output).</summary>
    void UpdateTokens(int inputTokens, int outputTokens);

    /// <summary>Live context used vs. auto-compact threshold.</summary>
    void UpdateContext(int contextUsedTokens, int? autoCompactThreshold);

    /// <summary>Right-side label: <c>"provider · model"</c> or <c>"model"</c>.</summary>
    void UpdateModel(string providerName, string model);

    /// <summary>
    /// Shows an error in the right slot. <paramref name="isPersistent"/> chooses
    /// the highlight (red vs. yellow). The next <see cref="ClearError"/> call
    /// (driven by a state change with <c>null</c> LastError) restores the
    /// model/path/tokens display.
    /// </summary>
    void ShowError(string message, bool isPersistent);

    /// <summary>Removes any active error from the right slot.</summary>
    void ClearError();

    /// <summary>
    /// Adds a persistent error marker to the chat transcript. Routed by
    /// <see cref="SessionStatusRouter"/> for non-transient errors only; the
    /// router dedups on message equality so a single failure never produces
    /// multiple transcript lines.
    /// </summary>
    void RecordPersistentError(string message);
}