namespace Phi.Extensions.Host;

/// <summary>
/// No-op <see cref="IUiSink"/> for headless contexts (CI / automation /
/// unit tests). <see cref="HasUi"/> is <c>false</c>; all dialog methods
/// return their no-op defaults; <see cref="Notify"/>,
/// <see cref="NotifyStatus"/>, <see cref="FlashError"/>,
/// <see cref="SubmitTranscriptLine"/> silently discard.
/// <para>
/// Wrapped by <see cref="PhiUiBridge"/> when the composition root has no
/// real UI to hand the runtime (or for tests that want to verify
/// <c>api.Context.Ui.HasUi == false</c> short-circuiting in extensions).
/// </para>
/// </summary>
public sealed class NullUiSink : IUiSink
{
    public bool HasUi => false;

    public void Notify(string message, NotifyLevel level) { }
    public void NotifyStatus(string message) { }
    public void FlashError(string message, bool persistent) { }
    public void SubmitTranscriptLine(TranscriptLine line) { }
    public void SubmitCustomMessageLine(string customType, string content, IReadOnlyDictionary<string, object?>? details) { }

    public Task<string?> ShowSelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout)
        => Task.FromResult<string?>(null);

    public Task<bool> ShowConfirmAsync(string title, string message, TimeSpan? timeout)
        => Task.FromResult(false);

    public Task<string?> ShowInputAsync(string title, string placeholder, TimeSpan? timeout)
        => Task.FromResult<string?>(null);
}
