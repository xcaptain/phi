using PhiAgent;
using PhiCoding.Providers;
using PhiCoding.Sessions;
using PhiCoding.Tui.Components;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace PhiCoding.Tui.Pages;

/// <summary>
/// The session detail page (<c>/sessions/:id</c>): the working conversation.
/// Owns the transcript and status bar; composes a <see cref="PromptInput"/>
/// for the editor, suggestion strip, slash commands, dialogs, and skill
/// completion. Bound to the session's reactive events.
/// <para>
/// When reached by promoting a fresh new-session page, the first prompt is
/// already in flight; the pending submission (carried on the navigator) is
/// rendered as the user bubble so the conversation is complete.
/// </para>
/// </summary>
public sealed class SessionPage : IPage
{
    private readonly PromptInput _input;
    private string? _lastRoutedError;

    public SessionPage(
        ISession session, ISessionNavigator navigator, ProviderManager providers)
    {
        // The input owns editor + dispatch + dialogs. The page feeds it the
        // three callbacks so the input stays ignorant of the transcript and
        // the promotion strategy.
        _input = new PromptInput(
            session,
            navigator,
            providers,
            onSubmitted: OnSubmitted,
            showInfo: m => Transcript.AddInfo(m),
            showSteeringQueued: t => Transcript.AddUserMessage($"[queued · steering] {t}"));
    }

    /// <summary>The transcript rendered by this page (set by <see cref="Build"/>).</summary>
    public ChatTranscript Transcript { get; private set; } = null!;

    /// <summary>The status bar rendered by this page (set by <see cref="Build"/>).</summary>
    public PhiStatusBar StatusBar { get; private set; } = null!;

    /// <summary>The prompt input this page composes (exposed for tests).</summary>
    public PromptInput Input => _input;

    /// <summary>
    /// Surface for the input's prompt-submitted callback: add the user
    /// bubble in the transcript. For skills, also reset the rendered-message
    /// counter so the next state pass renders everything cleanly.
    /// </summary>
    private void OnSubmitted(string text, bool isSkill)
    {
        Transcript.AddUserMessage(text);
        if (isSkill)
            Transcript.ResetRenderedCount();
    }

    public Visual Build()
    {
        _input.Build();

        Transcript = new ChatTranscript();
        StatusBar = new PhiStatusBar(_input.Session.State.Model);

        Transcript.Bind(_input.Session);
        BindStatusBarToEngine(StatusBar, Transcript);

        // First prompt from the new-session page: the run is already in
        // flight, so render the submitted text as the user bubble here (the
        // session's State doesn't surface it until the turn ends).
        var pending = _input.TakePendingSubmission();
        if (pending is not null && _input.Session.State.IsRunning)
            Transcript.AddUserMessage(pending);

        var header = ChatHeader.Build(_input.Session);

        var root = new DockLayout()
            .Top(header)
            .Content(Transcript.Visual)
            .Bottom(new VStack(_input.Editor.Scrollable(), _input.SuggestionStrip.Visual, StatusBar.Visual).Spacing(0)
                .Margin(new Thickness(0, 1, 0, 0)))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        root.SetStyle(Theme.Key, Theme.Default);

        return root;
    }

    private void BindStatusBarToEngine(PhiStatusBar status, ChatTranscript transcript)
    {
        _input.Session.StateChanged += s =>
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

        _input.Session.HarnessEvent += e =>
        {
            if (e is HarnessErrorEvent he)
                RouteError(status, transcript, he.Message);
        };

        status.Running.Value = _input.Session.State.IsRunning;
        status.QueuedCount.Value = _input.Session.State.SteeringCount + _input.Session.State.FollowUpCount;
        status.UpdateStats(_input.Session.State.Stats);
        status.UpdateContext(_input.Session.State.ContextUsedTokens, _input.Session.State.AutoCompactThreshold);
        status.UpdateModel(_input.Session.State.ProviderName, _input.Session.State.Model);
        if (_input.Session.State.LastError is { Length: > 0 } initial)
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