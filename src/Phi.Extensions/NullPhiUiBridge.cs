namespace Phi.Extensions;

/// <summary>
/// No-op <see cref="IPhiUiBridge"/> used when no UI is attached
/// (headless CI, automation, unit tests). <see cref="HasUi"/> is
/// <c>false</c>; all dialogs return their no-op defaults
/// (<c>null</c> / <c>false</c> / <c>null</c>); <see cref="Notify"/>,
/// <see cref="NotifyStatus"/>, <see cref="FlashError"/> silently
/// discard; <see cref="SubmitTranscriptLine"/> drops the line.
/// <para>
/// Extensions that need real UI must check <see cref="HasUi"/> via
/// <c>api.Context.Ui.HasUi</c> — the bridge itself is always safe to call.
/// </para>
/// </summary>
public sealed class NullPhiUiBridge : IPhiUiBridge
{
    public bool HasUi => false;

    public void Notify(string message, NotifyLevel level = NotifyLevel.Info) { }
    public Task<string?> SelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout = null)
        => Task.FromResult<string?>(null);
    public Task<bool> ConfirmAsync(string title, string message, TimeSpan? timeout = null)
        => Task.FromResult(false);
    public Task<string?> InputAsync(string title, string placeholder = "", TimeSpan? timeout = null)
        => Task.FromResult<string?>(null);
    public void SubmitTranscriptLine(TranscriptLine line) { }
    public void NotifyStatus(string message) { }
    public void FlashError(string message, bool persistent) { }
}
