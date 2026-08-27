using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Phi.Tui.Components;

/// <summary>
/// Builds the fixed header chrome: the phi logo on the left, the session's
/// provider/model on the right (updated reactively via <see cref="ISession.StateChanged"/>).
/// Used by the chat page.
/// </summary>
public static class ChatHeader
{
    /// <summary>Builds the header visual for the given session.</summary>
    public static Visual Build(ISession session, TuiUiThread? uiThread = null)
    {
        // Session.StateChanged can fire from the streaming provider's
        // worker thread (after a /connect, /models, or run completion).
        // Writing to Markup.Text without marshalling throws
        // "Invalid thread access" — route the mutation through the UI
        // dispatcher's Post so the visual update lands on the right
        // thread. Tests pass TuiUiThread.None to keep synchronous
        // behaviour.
        var marshaller = uiThread ?? TuiUiThread.None;
        var modelMarkup = new Markup(
            $"[dim]{FormatModel(session.State.ProviderName, session.State.Model)}[/]")
        {
            Wrap = false,
        };
        session.StateChanged += s =>
            marshaller.Post(() =>
                modelMarkup.Text = $"[dim]{FormatModel(s.ProviderName, s.Model)}[/]");

        return new Header
        {
            Left = new Markup("[bold]phi[/]") { Wrap = false },
            Right = modelMarkup,
        };
    }

    private static string FormatModel(string providerName, string model) =>
        providerName.Length > 0 ? $"{providerName}/{model}" : model;
}
