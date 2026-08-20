using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Phi.Avalonia.Tests.Helpers;

/// <summary>
/// Synthesizes pointer events for unit tests. The Avalonia.Headless
/// backend doesn't ship a full input pipeline, so we hand-build a
/// <see cref="PointerPressedEventArgs"/> and raise it through the standard
/// routed-event API so handlers wired via <c>PointerPressed += ...</c>
/// see the press exactly as they would in a real window.
/// </summary>
internal static class PointerInputSimulator
{
    /// <summary>
    /// Synthesizes a left-button pointer press on <paramref name="target"/>
    /// at the control's local origin (0, 0). The press propagates through
    /// the standard event pipeline; handlers that read
    /// <see cref="PointerPointProperties.IsLeftButtonPressed"/> see true.
    /// </summary>
    public static void LeftClick(Control target)
    {
        if (target is not Visual visual)
            throw new ArgumentException("Target must be a Visual", nameof(target));

        // PointerType.Mouse + isPrimary=true makes IsLeftButtonPressed
        // round-trip correctly through the press→pressed handler chain.
        var pointer = new Pointer(
            id: Pointer.GetNextFreeId(),
            type: PointerType.Mouse,
            isPrimary: true);
        var properties = new PointerPointProperties(
            modifiers: RawInputModifiers.None,
            kind: PointerUpdateKind.LeftButtonPressed);
        var point = new PointerPoint(pointer, new Point(0, 0), properties);
        var args = new PointerPressedEventArgs(
            source: visual,
            pointer: pointer,
            rootVisual: visual,
            rootVisualPosition: new Point(0, 0),
            timestamp: (ulong)DateTime.UtcNow.Ticks,
            properties: properties,
            modifiers: KeyModifiers.None)
        {
            RoutedEvent = InputElement.PointerPressedEvent,
        };
        target.RaiseEvent(args);
    }
}
