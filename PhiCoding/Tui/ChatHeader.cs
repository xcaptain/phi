using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui;

/// <summary>
/// Builds the fixed header chrome: the phi logo on the left, the session's
/// provider/model on the right (updated reactively via <see cref="ISession.StateChanged"/>).
/// Shared by <see cref="PhiCoding.Pages.SessionPage"/> and the new-session
/// landing page since both render the same chrome.
/// </summary>
public static class ChatHeader
{
    /// <summary>Builds the header visual for the given session.</summary>
    public static Visual Build(ISession session)
    {
        var modelMarkup = new Markup(
            $"[dim]{FormatModel(session.State.ProviderName, session.State.Model)}[/]")
        {
            Wrap = false,
        };
        session.StateChanged += s =>
            modelMarkup.Text = $"[dim]{FormatModel(s.ProviderName, s.Model)}[/]";

        return new Header
        {
            Left = new Markup("[bold]phi[/]") { Wrap = false },
            Right = modelMarkup,
        };
    }

    private static string FormatModel(string providerName, string model) =>
        providerName.Length > 0 ? $"{providerName}/{model}" : model;
}