using Avalonia.Controls;

namespace PhiCoding.Avalonia.Tests.Helpers;

/// <summary>
/// Helpers for poking at <see cref="PhiCoding.Avalonia.Components.CollapsibleSection"/>
/// internals from tests. The section's public surface intentionally exposes
/// only the header/body content controls (see <c>HeaderContent</c> /
/// <c>BodyContent</c>); these helpers walk the visual tree to find the
/// Border that owns the pointer event handler so tests can synthesize a
/// click without reaching into private fields.
/// </summary>
internal static class AvaloniaTestHelpers
{
    /// <summary>
    /// Returns the header Border (the click target wrapping the DockPanel).
    /// Tests that want to simulate a click call <c>LeftClick</c> on this
    /// directly to verify pointer event handling.
    /// </summary>
    public static Border FindHeaderArea(PhiCoding.Avalonia.Components.CollapsibleSection section)
    {
        var root = (StackPanel)section.Content!;
        return (Border)root.Children[0];
    }

    /// <summary>
    /// Returns the body ContentControl so tests can assert visibility
    /// state transitions (the section's <c>IsExpanded</c> setter flips
    /// <c>IsVisible</c> on this host).
    /// </summary>
    public static ContentControl FindBodyHost(PhiCoding.Avalonia.Components.CollapsibleSection section)
    {
        var root = (StackPanel)section.Content!;
        return (ContentControl)root.Children[1];
    }
}