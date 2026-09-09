using Phi.Agent;
using Phi.Avalonia.Desktop;
using Phi.Avalonia.Desktop.Components.ChatLines;
using Phi.Avalonia.Desktop.ViewModels;
using Phi.Prompts;
using Phi.Providers;

namespace Phi.Avalonia.Desktop.Tests;

/// <summary>
/// In-memory <see cref="ISession"/> fake used by the chat-page VM
/// projection tests. Carries a mutable <see cref="State"/> the test
/// can edit; <see cref="StateChanged"/> fires after every
/// <see cref="Mutate"/> call so the subscription path is exercised.
/// </summary>
internal sealed class FakeSession : ISession
{
    public string Id { get; init; } = "fake";
    public string Cwd { get; init; } = "/tmp";
    public string SystemPrompt { get; set; } = "";
    public bool HasUi { get; set; }
    public IReadOnlyList<SkillDescriptor> Skills => [];
    public IReadOnlyList<string> AvailableProviders => [];

    public SessionState State { get; private set; } = SessionState.Empty;

    public event Action<SessionState>? StateChanged;
    public event Action<HarnessEvent>? HarnessEvent;

    /// <summary>Test helper: raises <see cref="HarnessEvent"/> so the
    /// declared-but-never-raised warning stays off and future tests can
    /// exercise the streaming projection path.</summary>
    public void RaiseHarnessEvent(HarnessEvent e) => HarnessEvent?.Invoke(e);

    /// <summary>Test helper: replace <see cref="State"/> and fire
    /// <see cref="StateChanged"/>.</summary>
    public void Mutate(SessionState next)
    {
        State = next;
        StateChanged?.Invoke(next);
    }

    public void SubmitPrompt(string text) { }
    public void Cancel() { }
    public void EnqueueSteering(UserMessage message) { }
    public void EnqueueFollowUp(UserMessage message) { }
    public void RenameSession(string? title) { }
    public Task<string> LoadSkillAsync(string name, string? prompt = null) => Task.FromResult(name);
    public void SwitchModel(string model) { }
    public void SwitchProvider(IPhiProvider provider, string providerName, string model) { }
    public Task<ISession> NewSessionAsync(string? cwd = null) => Task.FromResult<ISession>(this);
    public void ReloadExtensions() { }
    public Task<ISession> ResumeAsync(string sessionId) => Task.FromResult<ISession>(this);
    public IReadOnlyList<Phi.SessionRecord> ListRecent(int days = 7) => [];
    public int DisposeCount { get; private set; }
    public void Dispose() => DisposeCount++;
}

/// <summary>
/// <see cref="ChatPageViewModel"/>: subscribes to
/// <see cref="ISession.StateChanged"/> and projects
/// <see cref="IAgentMessage"/>s from <c>session.State.Messages</c>
/// onto chat-line VMs in <see cref="ChatPageViewModel.Transcript"/>.
/// </summary>
public class ChatPageViewModelTests
{
    [Test]
    public async Task Constructor_WithNullSession_LeavesTranscriptEmpty()
    {
        var providers = new ProviderManager();
        var vm = new ChatPageViewModel(providers, session: null);

        await Assert.That(vm.Transcript.Count).IsEqualTo(0);
        await Assert.That(vm.HeaderText).IsEqualTo("New Chat");
        await Assert.That(vm.PromptInput.IsWorkspaceLocked).IsFalse();
    }

    [Test]
    public async Task Constructor_WithSession_ProjectsInitialMessages()
    {
        var providers = new ProviderManager();
        var session = new FakeSession();
        session.Mutate(session.State with
        {
            Messages = [new UserMessage { Content = "hello" }],
            SessionTitle = "test session",
        });

        var vm = new ChatPageViewModel(providers, session);

        await Assert.That(vm.HeaderText).IsEqualTo("test session");
        await Assert.That(vm.Transcript.Count).IsEqualTo(1);
        await Assert.That(vm.Transcript[0]).IsAssignableFrom<UserTextLineViewModel>();
        await Assert.That(((UserTextLineViewModel)vm.Transcript[0]).Text).IsEqualTo("hello");
        await Assert.That(vm.PromptInput.IsWorkspaceLocked).IsTrue();
    }

    [Test]
    public async Task StateChanged_UpdatesHeaderAndTranscript()
    {
        var providers = new ProviderManager();
        var session = new FakeSession();
        var vm = new ChatPageViewModel(providers, session);

        session.Mutate(session.State with
        {
            Messages = [new UserMessage { Content = "after change" }],
            SessionTitle = "updated title",
        });

        await Assert.That(vm.HeaderText).IsEqualTo("updated title");
        await Assert.That(vm.Transcript.Count).IsEqualTo(1);
        await Assert.That(((UserTextLineViewModel)vm.Transcript[0]).Text).IsEqualTo("after change");
    }

    [Test]
    public async Task StateChanged_MirrorsIsRunningOnPromptInput()
    {
        var providers = new ProviderManager();
        var session = new FakeSession();
        var vm = new ChatPageViewModel(providers, session);

        session.Mutate(session.State with { IsRunning = true });
        await Assert.That(vm.PromptInput.IsRunning).IsTrue();

        session.Mutate(session.State with { IsRunning = false });
        await Assert.That(vm.PromptInput.IsRunning).IsFalse();
    }

    [Test]
    public async Task Transcript_ProjectsUserAssistantAndToolMessages()
    {
        var providers = new ProviderManager();
        var session = new FakeSession();
        session.Mutate(session.State with
        {
            Messages =
            [
                new UserMessage { Content = "list the directory" },
                new AssistantMessage
                {
                    Content = [new TextBlock("Let me check.")],
                    StopReason = StopReasons.Stop,
                },
                new BashExecutionMessage { Command = "ls", Output = "file.txt" },
                new ToolResultMessage
                {
                    ToolName = "Bash",
                    ToolCallId = "call-1",
                    Content = [new TextBlock("file.txt\n")],
                },
            ],
        });

        var vm = new ChatPageViewModel(providers, session);

        await Assert.That(vm.Transcript.Count).IsEqualTo(4);
        await Assert.That(vm.Transcript[0]).IsAssignableFrom<UserTextLineViewModel>();
        await Assert.That(vm.Transcript[1]).IsAssignableFrom<AssistantTextLineViewModel>();
        await Assert.That(vm.Transcript[2]).IsAssignableFrom<ToolCardLineViewModel>();
        await Assert.That(((ToolCardLineViewModel)vm.Transcript[2]).ToolName).IsEqualTo("Bash");
        await Assert.That(((ToolCardLineViewModel)vm.Transcript[2]).Summary).IsEqualTo("ls");
        await Assert.That(vm.Transcript[3]).IsAssignableFrom<ToolCardLineViewModel>();
        await Assert.That(((ToolCardLineViewModel)vm.Transcript[3]).ToolName).IsEqualTo("Bash");
    }
}
