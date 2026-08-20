using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Phi.Avalonia.Controls;

/// <summary>
/// A compact "⋮" (vertical ellipsis) trigger that opens a <see cref="MenuFlyout"/>
/// with caller-supplied items. Used on session rows and workspace rows so
/// management actions (rename / delete / new session) stay one click away
/// without cluttering the list with permanent buttons.
/// <para>
/// The trigger behaves like an icon button (transparent + a <c>:pointerover</c>
/// chip background) but is a <see cref="Border"/> rather than a
/// <see cref="Button"/>: transparent + 0-border Buttons in Avalonia 12 have
/// flaky hit-testing — only part of their bounds reliably receives clicks
/// (see <c>CollapsibleSection</c>; verified here by a synthetic pointer
/// press reaching a Border's handler while a Button's drops it). A Border's
/// full bounds are the stable equivalent.
/// </para>
/// <para>
/// Dismissal is handled explicitly rather than relying on the popup's
/// light-dismiss: while the menu is open, any pointer press that reaches the
/// top level (i.e. outside the popup, which lives in its own layer / window)
/// closes the menu. This is deterministic across overlay and windowed popups.
/// </para>
/// </summary>
public partial class EllipsisMenu : UserControl
{
    private readonly MenuFlyout _flyout = new();
    private TopLevel? _dismissTopLevel;

    public EllipsisMenu()
    {
        InitializeComponent();
        _flyout.Opened += (_, _) => AttachDismiss();
        _flyout.Closed += (_, _) => DetachDismiss();
    }

    /// <summary>The menu this trigger opens (tests).</summary>
    internal MenuFlyout Menu => _flyout;

    private void OnTriggerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Left-button only — right-click context menus / middle-clicks
        // shouldn't toggle the row menu.
        if (!e.GetCurrentPoint(Trigger).Properties.IsLeftButtonPressed) return;
        if (_flyout.IsOpen)
            _flyout.Hide();
        else
            _flyout.ShowAt(this);
        // Swallow the press so it doesn't leak into row selection in the
        // sessions list.
        e.Handled = true;
    }

    // ──────── Dismiss on outside press ────────
    // Popup light-dismiss is unreliable across overlay / windowed popups, so
    // hook the top level instead: any pointer press that reaches it (i.e.
    // not inside the popup) closes the menu.

    private void AttachDismiss()
    {
        _dismissTopLevel = TopLevel.GetTopLevel(this);
        if (_dismissTopLevel is null) return;
        _dismissTopLevel.AddHandler(
            InputElement.PointerPressedEvent,
            OnTopLevelPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void DetachDismiss()
    {
        if (_dismissTopLevel is null) return;
        _dismissTopLevel.RemoveHandler(InputElement.PointerPressedEvent, OnTopLevelPressed);
        _dismissTopLevel = null;
    }

    private void OnTopLevelPressed(object? sender, PointerPressedEventArgs e)
    {
        // The press that opened the menu bubbles up from the trigger after
        // OnTriggerPressed handled it — skip it, every other press is outside.
        if (e.Source is Visual { } source && this.IsVisualAncestorOf(source))
            return;
        _flyout.Hide();
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