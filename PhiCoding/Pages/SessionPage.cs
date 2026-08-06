using PhiAgent;
using PhiCoding.Providers;
using PhiCoding.Sessions;
using PhiCoding.Tui;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace PhiCoding.Pages;

/// <summary>
/// The session detail page (<c>/sessions/:id</c>): the working conversation.
/// A <see cref="ChatTranscript"/> on top, the editor + suggestion strip +
/// status bar at the bottom, bound to the session's reactive events.
/// <para>
/// When reached by promoting a fresh new-session page, the first prompt is
/// already running; the pending submission (carried on the navigator) is
/// rendered as the user bubble so the conversation is complete.
/// </para>
/// </summary>
public sealed partial class SessionPage : ChatScreen
{
    private string? _lastRoutedError;

    public SessionPage(
        ISession session, ISessionNavigator navigator, ProviderManager providers)
        : base(session, navigator, providers)
    {
    }

    /// <summary>The transcript rendered by this page (set by <see cref="ChatScreen.Build"/>).</summary>
    public ChatTranscript Transcript { get; private set; } = null!;

    /// <summary>The status bar rendered by this page (set by <see cref="ChatScreen.Build"/>).</summary>
    public PhiStatusBar StatusBar { get; private set; } = null!;

    protected override Visual BuildLayout()
    {
        var transcript = new ChatTranscript();
        Transcript = transcript;
        var status = new PhiStatusBar(_session.State.Model);
        StatusBar = status;

        transcript.Bind(_session);
        BindStatusBarToEngine(status, transcript);

        // First prompt from the new-session page: the run is already in
        // flight, so render the submitted text as the user bubble here (the
        // session's State doesn't surface it until the turn ends).
        var pending = _navigator.TakePendingSubmission();
        if (pending is not null && _session.State.IsRunning)
            transcript.AddUserMessage(pending);

        var header = BuildHeader();

        var root = new DockLayout()
            .Top(header)
            .Content(transcript.Visual)
            .Bottom(new VStack(Editor.Scrollable(), SuggestionStrip.Visual, status.Visual).Spacing(0)
                .Margin(new Thickness(0, 1, 0, 0)))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        root.SetStyle(Theme.Key, Theme.Default);

        return root;
    }

    protected override void ShowInfo(string message) => Transcript.AddInfo(message);

    protected override void OnSubmitted(string text, bool isSkill)
    {
        Transcript.AddUserMessage(text);
        if (isSkill)
            Transcript.ResetRenderedCount();
    }

    protected override void ShowSteeringQueued(string text) =>
        Transcript.AddUserMessage($"[queued · steering] {text}");

    // ──────── Engine bindings ────────

    private void BindStatusBarToEngine(PhiStatusBar status, ChatTranscript transcript)
    {
        _session.StateChanged += s =>
        {
            status.Running.Value = s.IsRunning;
            status.QueuedCount.Value = s.SteeringCount + s.FollowUpCount;
            status.UpdateStats(s.Stats);
            status.UpdateContext(s.ContextUsedTokens, s.AutoCompactThreshold);
            status.UpdateModel(s.ProviderName, s.Model);

            // Event-driven error clear: any state change without a new
            // LastError wipes the previous error from the status bar.
            // A non-empty LastError replaces whatever is currently shown.
            if (s.LastError is { Length: > 0 } err)
                RouteError(status, transcript, err);
            else
            {
                // Clean state (e.g. a new run started and cleared LastError):
                // restore the status bar and reset dedup so a *new*
                // occurrence of the same error message gets a fresh
                // transcript record.
                status.ClearError();
                _lastRoutedError = null;
            }
        };

        _session.HarnessEvent += e =>
        {
            if (e is HarnessErrorEvent he)
                RouteError(status, transcript, he.Message);
        };

        status.Running.Value = _session.State.IsRunning;
        status.QueuedCount.Value = _session.State.SteeringCount + _session.State.FollowUpCount;
        status.UpdateStats(_session.State.Stats);
        status.UpdateContext(_session.State.ContextUsedTokens, _session.State.AutoCompactThreshold);
        status.UpdateModel(_session.State.ProviderName, _session.State.Model);
        if (_session.State.LastError is { Length: > 0 } initial)
            RouteError(status, transcript, initial);
    }

    /// <summary>
    /// Classifies an error and routes it: every error goes to the status bar,
    /// persistent errors additionally leave a transcript line so the user
    /// can scroll back to them after the status bar clears.
    /// The same message re-arriving on a later state change (LastError stays
    /// set until the next run clears it) is deduplicated — it updates the
    /// status bar but does not append a second transcript record.
    /// </summary>
    private void RouteError(PhiStatusBar status, ChatTranscript transcript, string message)
    {
        var isTransient = ErrorClassifier.LooksTransient(message);
        status.ShowError(message, isPersistent: !isTransient);
        if (isTransient) return;
        if (_lastRoutedError == message) return;
        _lastRoutedError = message;
        transcript.AddPersistentError(message);
    }
}
