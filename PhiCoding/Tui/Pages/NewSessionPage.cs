using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Sessions;
using PhiCoding.Tui.Components;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace PhiCoding.Tui.Pages;

/// <summary>
/// The new-session landing page (<c>/sessions/new</c>): structurally
/// identical to <see cref="SessionPage"/> — header on top, the same
/// <see cref="ChatTranscript"/> component in the content slot (empty
/// conversation + transient region showing a slogan), editor + suggestion
/// strip + status bar at the bottom. The first submitted prompt starts the
/// session's run and immediately promotes to the session's detail route
/// (<c>/sessions/:id</c>), carrying the submitted text as the pending
/// submission so the detail page can render the user bubble.
/// </summary>
public sealed class NewSessionPage : IPage
{
    private readonly PromptInput _input;

    public NewSessionPage(
        ISession session, ISessionNavigator navigator, ProviderManager providers)
    {
        Transcript = new ChatTranscript();
        _input = new PromptInput(session, navigator, providers, Transcript, OnSubmitted);
    }

    /// <summary>The conversation + transient-status view rendered by this page.</summary>
    public ChatTranscript Transcript { get; }

    /// <summary>The prompt input this page composes (exposed for tests).</summary>
    public PromptInput Input => _input;

    /// <summary>The bottom status bar (model/provider + cwd), set by <see cref="Build"/>.</summary>
    public PhiStatusBar StatusBar { get; private set; } = null!;

    /// <summary>
    /// The first prompt starts the run; promote to this session's detail
    /// route, carrying the text so the detail page renders the user bubble.
    /// </summary>
    private void OnSubmitted(string text, bool isSkill)
    {
        _input.Navigator.SetPendingSubmission(text);
        _ = _input.Navigator.NavigateAsync(
            new ChatRoute(new ExistingSessionRequest(_input.Session.State.SessionId)));
    }

    public Visual Build()
    {
        _input.Build();

        // Bottom status bar: model/provider + current directory. Bound to the
        // session's state so /connect and /models switches reflect immediately
        // (stats/context are empty until the first run, which promotes away).
        StatusBar = new PhiStatusBar(_input.Session.State.Model);
        _input.Session.StateChanged += s =>
        {
            StatusBar.UpdateModel(s.ProviderName, s.Model);
            StatusBar.UpdateStats(s.Stats);
            StatusBar.UpdateContext(s.ContextUsedTokens, s.AutoCompactThreshold);
        };

        // The landing content slot: an empty conversation with a slogan in the
        // transcript's transient region. Dialog feedback replaces the slogan
        // transiently; future landing content (news, usage help) can render
        // here instead.
        Transcript.ShowTransient("Phi — a minimal and portable coding agent");

        var root = new DockLayout()
            .Top(ChatHeader.Build(_input.Session))
            .Content(Transcript.Visual)
            .Bottom(new VStack(_input.Editor.Scrollable(), _input.SuggestionStrip.Visual, StatusBar.Visual).Spacing(0)
                .Margin(new Thickness(0, 1, 0, 0)))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        root.SetStyle(Theme.Key, Theme.Default);

        return root;
    }
}
