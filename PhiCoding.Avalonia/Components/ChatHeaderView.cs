using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhiCoding.Avalonia.Components;

/// <summary>
/// Top header chrome: the phi wordmark on the left, the active
/// provider/model on the right. Mirrors the TUI's <c>ChatHeader</c>.
/// </summary>
public sealed class ChatHeaderView
{
    private readonly TextBlock _modelLabel;

    public ChatHeaderView(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _modelLabel = new TextBlock { Foreground = AvaloniaTheme.TextSecondary };

        // Seed initial value, then keep the right-side label in sync with
        // session state (model switches via the prompt input's picker).
        UpdateLabel(session.State.ProviderName, session.State.Model);
        session.StateChanged += s => UpdateLabel(s.ProviderName, s.Model);

        var left = new TextBlock
        {
            Text = "phi",
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(_modelLabel, Dock.Right);
        _modelLabel.VerticalAlignment = VerticalAlignment.Center;

        Root = new DockPanel
        {
            Margin = new Thickness(12, 8),
            LastChildFill = true,
            Children = { left, _modelLabel },
        };
    }

    /// <summary>The top header visual.</summary>
    public Control Root { get; }

    private void UpdateLabel(string providerName, string model)
    {
        _modelLabel.Text = providerName.Length > 0 ? $"{providerName}/{model}" : model;
    }
}
