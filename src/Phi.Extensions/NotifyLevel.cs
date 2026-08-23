namespace Phi.Extensions;

/// <summary>
/// Severity for <see cref="IPhiUiBridge.Notify"/>. Maps to TUI toast
/// foreground color / Avalonia <c>DeskLog</c> styling.
/// </summary>
public enum NotifyLevel
{
    Info,
    Warning,
    Error,
}
