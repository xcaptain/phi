using PhiAgent;

namespace PhiCoding.Tui.Components;

/// <summary>
/// Wires a <see cref="PhiStatusBar"/> to a session's reactive state and
/// harness events. Errors are classified by <see cref="ErrorClassifier"/>:
/// every error reaches the status bar; persistent ones additionally leave a
/// transcript line so the user can scroll back to them after the status bar
/// clears. The same message re-arriving on a later state change is
/// deduplicated.
/// </summary>
internal static class StatusBarBinder
{
    public static void Bind(PhiStatusBar status, ChatTranscript transcript, ISession session)
    {
        var lastRoutedError = (string?)null;

        session.StateChanged += s =>
        {
            status.Running.Value = s.IsRunning;
            status.QueuedCount.Value = s.SteeringCount + s.FollowUpCount;
            status.UpdateStats(s.Stats);
            status.UpdateContext(s.ContextUsedTokens, s.AutoCompactThreshold);
            status.UpdateModel(s.ProviderName, s.Model);

            if (s.LastError is { Length: > 0 } err)
                RouteError(status, transcript, err, ref lastRoutedError);
            else
            {
                // Clean state (e.g. a new run cleared LastError): wipe the
                // status bar and reset dedup so a *new* occurrence of the
                // same error message gets a fresh transcript record.
                status.ClearError();
                lastRoutedError = null;
            }
        };

        session.HarnessEvent += e =>
        {
            if (e is HarnessErrorEvent he)
                RouteError(status, transcript, he.Message, ref lastRoutedError);
        };

        // Initial sync so a resumed session shows the right state at mount.
        status.Running.Value = session.State.IsRunning;
        status.QueuedCount.Value = session.State.SteeringCount + session.State.FollowUpCount;
        status.UpdateStats(session.State.Stats);
        status.UpdateContext(session.State.ContextUsedTokens, session.State.AutoCompactThreshold);
        status.UpdateModel(session.State.ProviderName, session.State.Model);
        if (session.State.LastError is { Length: > 0 } initial)
            RouteError(status, transcript, initial, ref lastRoutedError);
    }

    private static void RouteError(
        PhiStatusBar status, ChatTranscript transcript, string message, ref string? lastRoutedError)
    {
        var isTransient = ErrorClassifier.LooksTransient(message);
        status.ShowError(message, isPersistent: !isTransient);
        if (isTransient) return;
        if (lastRoutedError == message) return;
        lastRoutedError = message;
        transcript.AddPersistentError(message);
    }
}
