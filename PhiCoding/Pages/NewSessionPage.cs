using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Sessions;
using PhiCoding.Tui;
using PhiCoding.Tui.Inputs;
using PhiCoding.Tui.ToolCards;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;

namespace PhiCoding.Pages;

/// <summary>
/// The new-session landing page (<c>/sessions/new</c>): a centered prompt
/// editor with no transcript yet. Composes a <see cref="PromptInput"/> for
/// the editor, slash commands, and dialogs. The first submitted prompt
/// starts the session's run and immediately promotes to the session's detail
/// route (<c>/sessions/:id</c>), carrying the submitted text as the
/// pending submission so the detail page can render the user bubble. Dialog
/// feedback surfaces in a transient status line under the editor.
/// </summary>
public sealed class NewSessionPage : IPage
{
    private readonly PromptInput _input;
    private readonly State<string?> _infoLine = new(null);

    public NewSessionPage(
        ISession session, ISessionNavigator navigator, ProviderManager providers)
    {
        _input = new PromptInput(
            session,
            navigator,
            providers,
            onSubmitted: OnSubmitted,
            showInfo: m => _infoLine.Value = m,
            showSteeringQueued: _ => { /* a fresh session is never running */ });
    }

    /// <summary>The prompt input this page composes (exposed for tests).</summary>
    public PromptInput Input => _input;

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

        // Transient feedback line (dialog results, errors) driven by a State.
        var infoRegion = new ComputedVisual(() =>
        {
            var message = _infoLine.Value;
            return message is { Length: > 0 }
                ? new Markup($"[dim]{ToolCardBase.Escape(message)}[/]") { Wrap = true }
                : null;
        });

        var column = new VStack(
                _input.Editor.Scrollable(),
                _input.SuggestionStrip.Visual,
                infoRegion)
            .Spacing(1)
            .MaxWidth(88);

        var root = new DockLayout()
            .Top(ChatHeader.Build(_input.Session))
            .Content(new Center(column))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        root.SetStyle(Theme.Key, Theme.Default);

        return root;
    }
}