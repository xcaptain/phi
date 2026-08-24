using Phi.Extensions;
using Phi.Extensions.Host;
using Phi.Tui.Components;

namespace Phi.Tui;

/// <summary>
/// TUI implementation of <see cref="IUiSink"/>. Wires <see cref="IPhiUiBridge"/>
/// methods to the existing TUI primitives: <see cref="ChatTranscript"/> for
/// transient + transcript-line output, <see cref="PhiStatusBar"/> for status
/// / errors. Dialog methods defer to <see cref="TuiDialogShower"/> so the
/// real modal-dialog plumbing lives in <c>PromptInput.Dialogs</c> (where
/// the built-in slash commands already live) — the bridge just hands off.
/// <para>
/// Constructed once by the TUI composition root (<c>Program.cs</c>) and
/// passed to <see cref="PhiUiBridge"/>, which becomes the
/// <see cref="ExtensionRuntime"/>'s UI bridge.
/// </para>
/// </summary>
internal sealed class TuiUiSink : IUiSink
{
    private readonly ChatTranscript _transcript;
    private readonly PhiStatusBar _statusBar;
    private readonly TuiDialogShower _dialogShower;

    public bool HasUi => true;

    public TuiUiSink(ChatTranscript transcript, PhiStatusBar statusBar, TuiDialogShower dialogShower)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(statusBar);
        ArgumentNullException.ThrowIfNull(dialogShower);
        _transcript = transcript;
        _statusBar = statusBar;
        _dialogShower = dialogShower;
    }

    public void Notify(string message, NotifyLevel level)
    {
        // Promote Notify to the transcript as a transient line (matches
        // extension call sites like PermissionGate's "blocked" notice).
        // The status bar is reserved for errors only (see PhiStatusBar).
        var prefix = level switch
        {
            NotifyLevel.Warning => "⚠",
            NotifyLevel.Error => "✗",
            _ => "ℹ",
        };
        _transcript.ShowTransient($"{prefix} {message}");
    }

    public void NotifyStatus(string message)
    {
        // Status-bar slot is reserved for errors (PhiStatusBar only exposes
        // ShowError/ClearError); route transient status text to the
        // transcript transient line. Extensions calling NotifyStatus want
        // a low-priority info blurb, which is what this surface provides.
        _transcript.ShowTransient(message);
    }

    public void FlashError(string message, bool persistent)
        => _statusBar.ShowError(message, persistent);

    public void SubmitTranscriptLine(TranscriptLine line)
    {
        // Sprint 4: the line lands in the projector as a CustomLine. The
        // transcript renderer dispatches by LineType to whatever renderer
        // the extension registered; without one it falls back to a plain
        // text bubble. (Before Sprint 4 this routed to the persistent
        // error slot, which polluted the transcript with non-error lines.)
        _transcript.SubmitCustomLine(line);
    }

    public Task<string?> ShowSelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout)
        => _dialogShower.ShowSelectAsync(title, options, timeout);

    public Task<bool> ShowConfirmAsync(string title, string message, TimeSpan? timeout)
        => _dialogShower.ShowConfirmAsync(title, message, timeout);

    public Task<string?> ShowInputAsync(string title, string placeholder, TimeSpan? timeout)
        => _dialogShower.ShowInputAsync(title, placeholder, timeout);
}
