using Avalonia.Controls;
using Avalonia.Media;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace PhiCoding.Avalonia.Components.ToolCards;

/// <summary>
/// Bash tool body: stdout (default mono) + stderr (mono,
/// <see cref="AvaloniaTheme.Danger"/>) stacked vertically. The command
/// itself lives in the card's collapsed header, so this view carries no
/// copy-to-clipboard button — keeping the body minimal and aligned with
/// every other tool's detail shape (a scrollable content frame).
/// <para>
/// Empty states still render a dim <c>(no output)</c> hint so the body
/// always has at least one visible row.
/// </para>
/// </summary>
public sealed class BashOutputView : StackPanel
{
    public BashOutputView(string stdout, string stderr)
    {
        base.Orientation = global::Avalonia.Layout.Orientation.Vertical;
        base.Spacing = 6;

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
