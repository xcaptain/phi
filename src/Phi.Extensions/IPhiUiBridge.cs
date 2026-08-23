namespace Phi.Extensions;

/// <summary>
/// Bridge between an extension (which lives UI-framework-free) and the
/// running host's UI. The TUI's <c>TuiPhiUiBridge</c> and the Avalonia
/// shell's <c>AvaloniaPhiUiBridge</c> implement this; a headless
/// environment (CI / automation) uses <see cref="NullPhiUiBridge"/>.
/// <para>
/// Sprint 0 declares the contract only — TUI / Avalonia implementations
/// land in Sprint 3 (UI Bridge). Extensions can already be written and
/// loaded against <see cref="NullPhiUiBridge"/>; their dialog calls
/// return no-op defaults so unit tests don't need a real UI.
/// </para>
/// </summary>
public interface IPhiUiBridge
{
    /// <summary>
    /// Whether a real UI is attached. <c>false</c> means
    /// <see cref="SelectAsync"/> / <see cref="ConfirmAsync"/> /
    /// <see cref="InputAsync"/> return their no-op defaults
    /// (<c>null</c> / <c>false</c> / <c>null</c> respectively). Extensions
    /// can short-circuit UI work by checking this — but the bridge itself
    /// already handles <c>false</c> gracefully.
    /// </summary>
    bool HasUi { get; }

    // ──────── Notifications (fire-and-forget) ────────

    /// <summary>
    /// Show a transient notification (TUI toast / Avalonia desk-log
    /// entry). Surfaces immediately; <paramref name="message"/> should be
    /// one line of plain text.
    /// </summary>
    void Notify(string message, NotifyLevel level = NotifyLevel.Info);

    // ──────── Dialogs (async; no-op defaults when no UI) ────────

    /// <summary>
    /// Show a picker dialog with <paramref name="options"/>; return the
    /// selected option or <c>null</c> if cancelled / timed out / no UI.
    /// </summary>
    /// <param name="title"></param>
    /// <param name="options"></param>
    /// <param name="timeout">
    /// Optional auto-dismiss timeout (pi-style). <c>null</c> = wait
    /// indefinitely.
    /// </param>
    Task<string?> SelectAsync(
        string title,
        IReadOnlyList<string> options,
        TimeSpan? timeout = null);

    /// <summary>
    /// Show a yes/no confirmation; return the answer or <c>false</c> on
    /// cancel / timeout / no UI.
    /// </summary>
    Task<bool> ConfirmAsync(
        string title,
        string message,
        TimeSpan? timeout = null);

    /// <summary>
    /// Show a single-line text input dialog; return the entered text or
    /// <c>null</c> on cancel / timeout / no UI.
    /// </summary>
    Task<string?> InputAsync(
        string title,
        string placeholder = "",
        TimeSpan? timeout = null);

    // ──────── Transcript ────────

    /// <summary>
    /// Inject a <see cref="TranscriptLine"/> into the host's transcript
    /// projector (subject to the registered renderer for <c>line.Type</c>;
    /// falls back to a plain text rendering if no renderer is registered).
    /// </summary>
    void SubmitTranscriptLine(TranscriptLine line);

    // ──────── Status bar / errors (Phi-specific) ────────

    /// <summary>Show a status-bar message (transient, not a transcript line).</summary>
    void NotifyStatus(string message);

    /// <summary>
    /// Flash an error in the status bar. <paramref name="persistent"/>
    /// keeps it visible until the user dismisses (vs. fade-out).
    /// </summary>
    void FlashError(string message, bool persistent);
}
