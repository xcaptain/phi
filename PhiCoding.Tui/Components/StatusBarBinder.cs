using PhiCoding.Status;

namespace PhiCoding.Tui.Components;

/// <summary>
/// Wires a <see cref="PhiStatusBar"/> + <see cref="ChatTranscript"/> pair to a
/// session's reactive state. Thin adapter that delegates the routing logic
/// to <see cref="SessionStatusRouter"/>; the only TUI-specific bit is the
/// <see cref="ISessionStatusSink"/> implementation that splits
/// <c>RecordPersistentError</c> off to the transcript.
/// </summary>
internal static class StatusBarBinder
{
    public static void Bind(PhiStatusBar status, ChatTranscript transcript, ISession session)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(session);

        SessionStatusRouter.Bind(session, new TuiStatusSink(status, transcript));
    }

    /// <summary>
    /// Status sink that drives the TUI's <see cref="PhiStatusBar"/> for the
    /// ephemeral status surface and writes persistent errors into the chat
    /// <see cref="ChatTranscript"/>. Identical routing — transient vs.
    /// persistent classification, dedup — happens upstream in
    /// <see cref="SessionStatusRouter"/>; this class only dispatches.
    /// </summary>
    private sealed class TuiStatusSink : ISessionStatusSink
    {
        private readonly PhiStatusBar _bar;
        private readonly ChatTranscript _transcript;

        public TuiStatusSink(PhiStatusBar bar, ChatTranscript transcript)
        {
            _bar = bar;
            _transcript = transcript;
        }

        public void SetRunning(bool isRunning) => _bar.Running.Value = isRunning;

        public void SetTurn(int turn) => _bar.SetTurn(turn);

        public void SetQueuedCount(int count) => _bar.QueuedCount.Value = count;

        public void UpdateTokens(int inputTokens, int outputTokens)
        {
            var total = inputTokens + outputTokens;
            _bar.UpdateStats(new SessionStats(0, 0, inputTokens, outputTokens, total, null));
        }

        public void UpdateContext(int contextUsedTokens, int? autoCompactThreshold)
            => _bar.UpdateContext(contextUsedTokens, autoCompactThreshold);

        public void UpdateModel(string providerName, string model)
            => _bar.UpdateModel(providerName, model);

        public void ShowError(string message, bool isPersistent)
            => _bar.ShowError(message, isPersistent);

        public void ClearError() => _bar.ClearError();

        public void RecordPersistentError(string message)
            => _transcript.AddPersistentError(message);
    }
}