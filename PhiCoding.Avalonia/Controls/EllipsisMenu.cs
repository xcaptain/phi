using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace PhiCoding.Avalonia.Controls;

/// <summary>
/// A compact "⋮" (vertical ellipsis) button that opens a <see cref="MenuFlyout"/>
/// with caller-supplied items. Used on session rows and workspace rows so
/// management actions (rename / delete / new session) stay one click away
/// without cluttering the list with permanent buttons.
/// </summary>
public sealed class EllipsisMenu : Button
{
    private readonly MenuFlyout _flyout;

    public EllipsisMenu()
    {
        Content = new MaterialIcon
        {
            Kind = MaterialIconKind.DotsHorizontal,
            Width = 14,
            Height = 14,
            Foreground = AvaloniaTheme.TextSecondary,
        };
        Padding = new Thickness(4, 2);
        CornerRadius = new CornerRadius(4);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;

        _flyout = new MenuFlyout();
        Flyout = _flyout;
    }

    /// <summary>
    /// Adds one menu item. The label + optional action are wired to a
    /// <see cref="MenuItem"/>; selecting it closes the flyout and invokes
    /// <paramref name="onClick"/>.
    /// </summary>
    public EllipsisMenu AddItem(string header, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        _flyout.Items.Add(item);
        return this;
    }

    /// <summary>The number of items currently in the menu (tests).</summary>
    public int ItemCount => _flyout.Items.Count;
}
