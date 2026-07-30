using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Session contract. Frontends bind to state changes via
/// <see cref="StateChanged"/> and <see cref="HarnessEvent"/>, and dispatch
/// user actions through the action methods.
/// </summary>
public interface ISession
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
