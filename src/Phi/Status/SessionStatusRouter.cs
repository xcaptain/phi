using Phi.Agent;

namespace Phi.Status;

/// <summary>
/// Subscribes to a session's reactive state and routes the relevant values
/// into an <see cref="ISessionStatusSink"/>. Classifies errors via
/// <see cref="ErrorClassifier"/> so transient blips flash the status bar
/// without polluting the transcript; persistent failures reach the
/// transcript once (deduped on message equality) until the next run clears
/// <see cref="SessionState.LastError"/>.
/// <para>
/// This is the UI-agnostic core of the TUI's <c>StatusBarBinder</c>. TUI and
/// Desk each provide their own <see cref="ISessionStatusSink"/> adapter.
/// </para>
/// </summary>
public static class SessionStatusRouter
{
    /// <summary>
    /// Wires <paramref name="sink"/> to <paramref name="session"/> and runs an
    /// initial sync so a resumed session shows the right state immediately.
    /// </summary>
    public static void Bind(ISession session, ISessionStatusSink sink)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sink);

        var lastRoutedError = (string?)null;

        session.StateChanged += s =>
        {
            sink.SetRunning(s.IsRunning);
            sink.SetTurn(s.Turn);
            sink.SetQueuedCount(s.SteeringCount + s.FollowUpCount);
            sink.UpdateTokens(s.Stats.InputTokens, s.Stats.OutputTokens);
            sink.UpdateContext(s.ContextUsedTokens, s.AutoCompactThreshold);
            sink.UpdateModel(s.ProviderName, s.Model);

            if (s.LastError is { Length: > 0 } err)
                RouteError(sink, err, ref lastRoutedError);
            else
            {
                sink.ClearError();
                lastRoutedError = null;
            }
        };

        session.HarnessEvent += e =>
        {
            if (e is HarnessErrorEvent he)
                RouteError(sink, he.Message, ref lastRoutedError);
        };

        // Initial sync.
        sink.SetRunning(session.State.IsRunning);
        sink.SetTurn(session.State.Turn);
        sink.SetQueuedCount(session.State.SteeringCount + session.State.FollowUpCount);
        sink.UpdateTokens(session.State.Stats.InputTokens, session.State.Stats.OutputTokens);
        sink.UpdateContext(session.State.ContextUsedTokens, session.State.AutoCompactThreshold);
        sink.UpdateModel(session.State.ProviderName, session.State.Model);
        if (session.State.LastError is { Length: > 0 } initial)
            RouteError(sink, initial, ref lastRoutedError);
    }

    private static void RouteError(ISessionStatusSink sink, string message, ref string? lastRoutedError)
    {
        var isTransient = ErrorClassifier.LooksTransient(message);
        sink.ShowError(message, isPersistent: !isTransient);
        if (isTransient) return;
        if (lastRoutedError == message) return;
        lastRoutedError = message;
        sink.RecordPersistentError(message);
    }
}
