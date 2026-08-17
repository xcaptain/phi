using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace PhiCoding.Avalonia.Components.ToolCards;

/// <summary>
/// Standard scrollable body wrapper for every tool card's detail content.
/// Caps the rendered height at <see cref="DefaultMaxHeight"/> px so a long
/// <c>read</c> / <c>bash</c> output can't blow out the transcript; any
/// overflow turns into a vertical / horizontal scrollbar inside the
/// <see cref="ScrollViewer"/>.
/// <para>
/// The outer <see cref="Border"/> gives every tool card's detail area a
/// consistent shape (rounded corners, subtle background, hairline border)
/// so they read as a unified "details box" regardless of which tool
/// produced them.
/// </para>
/// </summary>
internal sealed class ToolCardBodyFrame : Border
{
    /// <summary>Hard cap on rendered body height (default 400 pixels).
    /// Long output scrolls inside the frame instead of stretching the
    /// transcript.</summary>
    public const double DefaultMaxHeight = 400;

    public ToolCardBodyFrame(Control content, double maxHeight = DefaultMaxHeight)
        : this(content, allowHorizontalScroll: true, maxHeight)
    {
    }

    public ToolCardBodyFrame(Control content, bool allowHorizontalScroll, double maxHeight = DefaultMaxHeight)
    {
        ArgumentNullException.ThrowIfNull(content);
        Padding = new Thickness(8);
        CornerRadius = new CornerRadius(6);
        Background = AvaloniaTheme.ContainerBackground;
        BorderBrush = AvaloniaTheme.ControlBorder;
        BorderThickness = new Thickness(1);
        MaxHeight = maxHeight;
        Child = new ScrollViewer
        {
            // With horizontal scrolling enabled, content is measured with
            // infinite width: text never wraps and Grid "star" columns size
            // to their content, which misaligns multi-block diffs and forces
            // the user to drag the scrollbar. Diff bodies disable it so the
            // content is constrained to the viewport width and wraps.
            HorizontalScrollBarVisibility = allowHorizontalScroll
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(4),
            Content = content,
        };
    }
}
