using PhiAgent;
using PhiCoding.Prompts;

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

    /// <summary>Working directory this session is bound to.</summary>
    public string Cwd { get; set; } = "/cwd";

    public IReadOnlyList<SkillDescriptor> Skills { get; private set; } = [];

    /// <summary>Override to capture SubmitPrompt calls.</summary>
    public Action<string>? OnSubmitPrompt { get; set; }

    /// <summary>Override to capture Cancel calls.</summary>
    public Action? OnCancel { get; set; }

    /// <summary>Last submitted text (if SubmitPrompt was called).</summary>
    public string? LastSubmittedText { get; private set; }

    /// <summary>Whether Cancel was called.</summary>
    public bool CancelCalled { get; private set; }

    /// <summary>Whether Dispose was called.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Last model passed to <see cref="SwitchModel"/> or <see cref="SwitchProvider"/>.</summary>
    public string? LastSwitchedModel { get; private set; }

    /// <summary>Last provider passed to <see cref="SwitchProvider"/>, or null.</summary>
    public IPhiProvider? LastSwitchedProvider { get; private set; }

    /// <summary>Last provider name passed to <see cref="SwitchProvider"/>, or null.</summary>
    public string? LastSwitchedProviderName { get; private set; }

    public void SubmitPrompt(string text)
    {
        LastSubmittedText = text;
        OnSubmitPrompt?.Invoke(text);
    }

    public void SwitchModel(string model)
    {
        LastSwitchedModel = model;
        State = State with { Model = model };
        StateChanged?.Invoke(State);
    }

    public void SwitchProvider(IPhiProvider provider, string providerName, string model)
    {
        LastSwitchedProvider = provider;
        LastSwitchedProviderName = providerName;
        LastSwitchedModel = model;
        State = State with { Model = model, ProviderName = providerName };
        StateChanged?.Invoke(State);
    }

    public void Cancel()
    {
        CancelCalled = true;
        OnCancel?.Invoke();
    }

    public void EnqueueSteering(UserMessage message) { }
    public void EnqueueFollowUp(UserMessage message) { }
    public void RenameSession(string? title) { }
    public Task<string> LoadSkillAsync(string name, string? prompt = null) => Task.FromResult(name);

    public void Dispose()
    {
        Disposed = true;
    }

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
