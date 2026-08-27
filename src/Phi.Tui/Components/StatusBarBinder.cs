using Phi.Status;

namespace Phi.Tui.Components;

/// <summary>
/// Wires a <see cref="PhiStatusBar"/> + <see cref="ChatTranscript"/> pair to a
/// session's reactive state. Thin adapter that delegates the routing logic
/// to <see cref="SessionStatusRouter"/>; the only TUI-specific bit is the
/// <see cref="ISessionStatusSink"/> implementation that splits
/// <c>RecordPersistentError</c> off to the transcript.
/// </summary>
internal static class StatusBarBinder
{
    public static void Bind(
        PhiStatusBar status,
        ChatTranscript transcript,
        ISession session,
        TuiUiThread? uiThread = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(session);

        // Ensure the transcript has a projector so AddPersistentError (which
        // the sink calls for non-transient errors) actually lands in the
        // flow. PhiTuiApp.BuildCurrentPage calls Bind too, but this keeps
        // tests that skip PhiTuiApp (e.g. PhiTuiAppTests) self-sufficient.
        transcript.Bind(session, renderers: null, uiThread: uiThread);
        SessionStatusRouter.Bind(
            session,
            new TuiStatusSink(status, transcript, uiThread ?? TuiUiThread.None));
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
        private readonly TuiUiThread _uiThread;

        public TuiStatusSink(PhiStatusBar bar, ChatTranscript transcript, TuiUiThread uiThread)
        {
            _bar = bar;
            _transcript = transcript;
            _uiThread = uiThread;
        }

        // Every method below mutates a XenoAtom visual (State<T>.Value
        // setter, DocumentFlow.Items.Add, etc.). The session's
        // StateChanged event fires from whatever thread the underlying
        // action runs on — the streaming provider's IO completion thread
        // for real LLMs, the calling thread for sync mocks. Without
        // marshalling, a real-LLM run would throw "Invalid thread access"
        // the moment the first token streams in. Post routes every sink
        // call through the TerminalApp dispatcher so the visual mutation
        // lands on the UI thread.

        public void SetRunning(bool isRunning) =>
            _uiThread.Post(() => _bar.Running.Value = isRunning);

        public void SetTurn(int turn) =>
            _uiThread.Post(() => _bar.SetTurn(turn));

        public void SetQueuedCount(int count) =>
            _uiThread.Post(() => _bar.QueuedCount.Value = count);

        public void UpdateTokens(int inputTokens, int outputTokens)
        {
            // Capture locals — they ride the closure into the marshalled
            // lambda. The session state object can change again before the
            // lambda fires, so we don't read it inside the lambda.
            var total = inputTokens + outputTokens;
            _uiThread.Post(() => _bar.UpdateStats(
                new SessionStats(0, 0, inputTokens, outputTokens, total, null)));
        }

        public void UpdateContext(int contextUsedTokens, int? autoCompactThreshold) =>
            _uiThread.Post(() =>
                _bar.UpdateContext(contextUsedTokens, autoCompactThreshold));

        public void UpdateModel(string providerName, string model) =>
            _uiThread.Post(() => _bar.UpdateModel(providerName, model));

        public void ShowError(string message, bool isPersistent) =>
            _uiThread.Post(() => _bar.ShowError(message, isPersistent));

        public void ClearError() =>
            _uiThread.Post(() => _bar.ClearError());

        public void RecordPersistentError(string message) =>
            _uiThread.Post(() => _transcript.AddPersistentError(message));
    }
}
