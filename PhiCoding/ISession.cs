using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Session contract. Frontends bind to state changes via
/// <see cref="StateChanged"/> and <see cref="HarnessEvent"/>, and dispatch
/// user actions through the action methods.
/// <para>
/// Implementing <see cref="IDisposable"/> signals the session owns scoped
/// resources (notably the in-flight run's <see cref="CancellationTokenSource"/>).
/// Dispose cancels any active run, awaits it briefly, and releases the
/// cancellation source. Frontends should dispose when the session's
/// lifecycle ends (e.g. TUI exit, switching to another session).
/// </para>
/// </summary>
public interface ISession : IDisposable
{
    event Action<SessionState>? StateChanged;
    event Action<HarnessEvent>? HarnessEvent;
    SessionState State { get; }

    void SubmitPrompt(string text);
    void Cancel();
    void EnqueueSteering(UserMessage message);
    void EnqueueFollowUp(UserMessage message);
    void RenameSession(string? title);
    Task ResumeSession(string sessionId);
    IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7);
}
