using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace PhiCoding.Desk.Components;

/// <summary>
/// Top header chrome: the phi wordmark on the left, the active
/// provider/model on the right. Mirrors the TUI's <c>ChatHeader</c>.
/// </summary>
public sealed class ChatHeaderView
{
    private readonly ObservableValue<string> _modelLabel = new(string.Empty);

    public ChatHeaderView(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Seed initial value, then keep the right-side label in sync with
        // session state (model switches via /models or /connect).
        UpdateLabel(session.State.ProviderName, session.State.Model);
        session.StateChanged += s => UpdateLabel(s.ProviderName, s.Model);

        var left = new Label().Text("phi").SemiBold().FontSize(16);
        var right = new Label()
            .BindText(_modelLabel)
            .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)));

        Root = new DockPanel()
            .LastChildFill()
            .Padding(12, 8)
            .Children(
                left.DockLeft(),
                right.DockRight());
    }

    /// <summary>The top header visual.</summary>
    public FrameworkElement Root { get; }

    private void UpdateLabel(string providerName, string model)
    {
        _modelLabel.Value = providerName.Length > 0 ? $"{providerName}/{model}" : model;
    }
}
