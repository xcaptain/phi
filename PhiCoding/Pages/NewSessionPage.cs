using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Sessions;
using PhiCoding.Tui;
using PhiCoding.Tui.ToolCards;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;

namespace PhiCoding.Pages;

/// <summary>
/// The new-session landing page (<c>/sessions/new</c>): a centered prompt
/// editor with no transcript yet. The first submitted prompt starts the
/// session's run and immediately promotes to the session's detail route
/// (<c>/sessions/:id</c>), carrying the submitted text as the pending
/// submission so the detail page's transcript can render the user bubble.
/// Dialog feedback surfaces in a transient status line under the editor.
/// </summary>
public sealed class NewSessionPage : ChatScreen
{
    private readonly State<string?> _infoLine = new(null);

    public NewSessionPage(
        ISession session, ISessionNavigator navigator, ProviderManager providers)
        : base(session, navigator, providers)
    {
    }

    protected override Visual BuildLayout()
    {
        // Transient feedback line (dialog results, errors) driven by a State.
        var infoRegion = new ComputedVisual(() =>
        {
            var message = _infoLine.Value;
            return message is { Length: > 0 }
                ? new Markup($"[dim]{ToolCardBase.Escape(message)}[/]") { Wrap = true }
                : null;
        });

        var column = new VStack(
                Editor.Scrollable(),
                SuggestionStrip.Visual,
                infoRegion)
            .Spacing(1);

        var root = new DockLayout()
            .Top(BuildHeader())
            .Content(new Center(column))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        root.SetStyle(Theme.Key, Theme.Default);

        return root;
    }

    protected override void ShowInfo(string message) => _infoLine.Value = message;

    /// <summary>
    /// The first prompt starts the run; promote to this session's detail
    /// route, carrying the text so the detail page renders the user bubble.
    /// </summary>
    protected override void OnSubmitted(string text, bool isSkill)
    {
        _navigator.SetPendingSubmission(text);
        _ = _navigator.NavigateAsync(
            new ChatRoute(new ExistingSessionRequest(_session.State.SessionId)));
    }
}
