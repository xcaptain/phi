using PhiAgent;

namespace PhiCoding.Tests.Helpers;

/// <summary>
/// In-memory <see cref="ISession"/> for TUI tests. Fires
/// <see cref="StateChanged"/> and <see cref="HarnessEvent"/> on demand;
/// action calls are recorded for assertions.
/// </summary>
public sealed class MockSession : ISession
{
    public event Action<SessionState>? StateChanged;
    public event Action<HarnessEvent>? HarnessEvent;

    public SessionState State { get; private set; } = SessionState.Empty;

    /// <summary>Override to capture SubmitPrompt calls.</summary>
    public Action<string>? OnSubmitPrompt { get; set; }

    /// <summary>Override to capture Cancel calls.</summary>
    public Action? OnCancel { get; set; }

    /// <summary>Last submitted text (if SubmitPrompt was called).</summary>
    public string? LastSubmittedText { get; private set; }

    /// <summary>Whether Cancel was called.</summary>
    public bool CancelCalled { get; private set; }

    public void SubmitPrompt(string text)
    {
        LastSubmittedText = text;
        OnSubmitPrompt?.Invoke(text);
    }

    public void Cancel()
    {
        CancelCalled = true;
        OnCancel?.Invoke();
    }

    public void EnqueueSteering(UserMessage message) { }
    public void EnqueueFollowUp(UserMessage message) { }
    public void RenameSession(string? title) { }
    public Task ResumeSession(string sessionId) => Task.CompletedTask;
    public IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7) => [];

    /// <summary>
    /// Fires <see cref="StateChanged"/> with a new state built from the
    /// current one plus <paramref name="update"/>.
    /// </summary>
    public void UpdateState(Func<SessionState, SessionState> update)
    {
        State = update(State);
        StateChanged?.Invoke(State);
    }

    /// <summary>
    /// Fires a <see cref="HarnessEvent"/> directly, as if the harness
    /// produced it during a turn.
    /// </summary>
    public void EmitHarnessEvent(HarnessEvent ev) => HarnessEvent?.Invoke(ev);

    /// <summary>
    /// Convenience: sets <see cref="State"/> with the given messages
    /// and fires <see cref="StateChanged"/>.
    /// </summary>
    public void SetMessages(params IAgentMessage[] messages)
    {
        State = State with { Messages = messages };
        StateChanged?.Invoke(State);
    }
}
