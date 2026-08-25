namespace Phi.Extensions.Host;

/// <summary>
/// UI-framework-free surface for hosting an extension's UI bridge. The
/// composition root's UI layer (TUI's <c>PromptInput</c> + dialogs, the
/// Avalonia shell) implements this; <see cref="PhiUiBridge"/> holds an
/// <see cref="IUiSink"/> and forwards every <see cref="IPhiUiBridge"/>
/// call to it. Tests inject a fake <see cref="IUiSink"/> to assert bridge
/// behavior without standing up a real UI.
/// <para>
/// All dialog methods are <c>async</c> and must complete the returned
/// task when the dialog closes (with the user's choice, or a no-op
/// default on cancel/timeout).
/// </para>
/// </summary>
public interface IUiSink
{
    /// <summary>
    /// Whether a real UI is attached. <c>false</c> means dialog methods
    /// must return their no-op defaults (<c>null</c> / <c>false</c> /
    /// <c>null</c>) without showing anything. <see cref="PhiUiBridge"/>
    /// surfaces this to extensions via <see cref="IPhiUiBridge.HasUi"/>.
    /// </summary>
    bool HasUi { get; }

    /// <summary>Show a transient notification (toast / desk-log / status bar).</summary>
    void Notify(string message, NotifyLevel level);

    /// <summary>Show a status-bar message (transient, not a transcript line).</summary>
    void NotifyStatus(string message);

    /// <summary>
    /// Flash an error in the status bar. <paramref name="persistent"/>
    /// keeps it visible until the user dismisses (vs. fade-out).
    /// </summary>
    void FlashError(string message, bool persistent);

    /// <summary>
    /// Submit a transcript line into the host's projector. The line is
    /// rendered by whatever renderer is registered for
    /// <see cref="TranscriptLine.Type"/> (Sprint 4); without a renderer
    /// the host falls back to plain text.
    /// </summary>
    void SubmitTranscriptLine(TranscriptLine line);

    /// <summary>
    /// Submit a custom-typed assistant message (<c>IPhiApi.SubmitCustomMessage</c>)
    /// into the host's projector. Rendered by whatever renderer is
    /// registered for <paramref name="customType"/> via
    /// <c>RegisterMessageRenderer</c>; without one the host falls back to
    /// plain text.
    /// </summary>
    void SubmitCustomMessageLine(
        string customType,
        string content,
        IReadOnlyDictionary<string, object?>? details);

    /// <summary>Show a picker; resolve with the selection or <c>null</c> on cancel.</summary>
    Task<string?> ShowSelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout);

    /// <summary>Show a yes/no confirmation; resolve with <c>false</c> on cancel.</summary>
    Task<bool> ShowConfirmAsync(string title, string message, TimeSpan? timeout);

    /// <summary>Show a single-line text input; resolve with the text or <c>null</c> on cancel.</summary>
    Task<string?> ShowInputAsync(string title, string placeholder, TimeSpan? timeout);
}
