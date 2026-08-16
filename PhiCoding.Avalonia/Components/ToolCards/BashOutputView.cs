using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace PhiCoding.Avalonia.Components.ToolCards;

/// <summary>
/// Bash tool body: command row (mono + copy button) on top, stdout
/// (default mono) + stderr (mono, <see cref="AvaloniaTheme.Danger"/>)
/// below. Pure layout / styling; no IO, no state. Used by
/// <see cref="BashToolCardView"/> as the expanded body of the bash card.
/// <para>
/// Empty states are rendered as a dim <c>(no output)</c> hint so the
/// body always has at least one visible row — a user clicking an
/// already-collapsed card and seeing absolutely nothing reads as a
/// "click did nothing" bug, even when the section toggled correctly.
/// </para>
/// </summary>
public sealed class BashOutputView : StackPanel
{
    public BashOutputView(string command, string stdout, string stderr)
    {
        Orientation = Orientation.Vertical;
        Spacing = 6;

        Children.Add(BuildCommandRow(command));

        if (stdout.Length > 0)
            Children.Add(BuildOutputBlock(stdout, danger: false));
        if (stderr.Length > 0)
            Children.Add(BuildOutputBlock(stderr, danger: true));

        if (stdout.Length == 0 && stderr.Length == 0)
            Children.Add(new TextBlock
            {
                Text = "(no output)",
                Foreground = AvaloniaTheme.TextSecondary,
                FontFamily = AvaloniaTheme.MonoFontFamily,
            });
    }

    /// <summary>
    /// Command row: <c>$ &lt;command&gt;</c> in a monospace TextBlock on
    /// the left, copy-to-clipboard button on the right. Wrapped in a
    /// Border with a faint background so it reads as a "command box"
    /// above the output, matching the maka design.
    /// </summary>
    private static Border BuildCommandRow(string command)
    {
        var commandText = new TextBlock
        {
            Text = string.IsNullOrEmpty(command) ? "(no command)" : $"$ {command}",
            FontFamily = AvaloniaTheme.MonoFontFamily,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var copyButton = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            Content = new MaterialIcon
            {
                Kind = MaterialIconKind.ContentCopy,
                Width = 14,
                Height = 14,
                Foreground = AvaloniaTheme.TextSecondary,
            },
        };
        ToolTip.SetTip(copyButton, "Copy command");
        copyButton.Click += (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(copyButton)?.Clipboard;
            clipboard?.SetTextAsync(command ?? "");
        };

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(copyButton, Dock.Right);
        row.Children.Add(copyButton);
        row.Children.Add(commandText);

        return new Border
        {
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(6),
            Background = AvaloniaTheme.ContainerBackground,
            BorderBrush = AvaloniaTheme.ControlBorder,
            BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    private static TextBlock BuildOutputBlock(string text, bool danger) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = AvaloniaTheme.MonoFontFamily,
            // Explicit Foreground brush. Setting it to null works in unit
            // tests but Avalonia's FluentTheme resolves a null Foreground
            // on a TextBlock nested inside a ContentControl-wrapped StackPanel
            // to transparent in some configurations — leaving the stdout
            // invisible even though it's measured to the right size. An
            // explicit theme brush sidesteps that resolution path.
            Foreground = danger ? AvaloniaTheme.Danger : AvaloniaTheme.TextPrimary,
        };
}