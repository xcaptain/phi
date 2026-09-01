using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Phi.Agent;
using Phi.Avalonia.Desktop2.Components.ChatLines;
using Phi.Providers;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Right-column chat region: header (session title), transcript, prompt
/// input. UI-2 + UI-3 prototype replaced by real
/// <see cref="ISession"/> projection: every
/// <see cref="IAgentMessage"/> in <c>session.State.Messages</c> is
/// rendered as a chat-line VM and surfaced through
/// <see cref="Transcript"/>.
/// <para>
/// When <paramref name="session"/> is null (pre-session state, or
/// after <see cref="ActiveSession.Clear"/>) the header shows a static
/// placeholder and the transcript is empty; the prompt input still
/// binds so the user can pick workspace + model and submit, which will
/// create the session via <c>Composition.NewSessionAsync</c>.
/// </para>
/// <para>
/// Lifecycle: the shell creates a fresh <see cref="ChatPageViewModel"/>
/// each time <see cref="ActiveSession.Changed"/> fires (sidebar New
/// Chat, future Resume). The old VM is no longer reachable; the new
/// one subscribes to <see cref="ISession.StateChanged"/> in its ctor
/// and projects the new <see cref="IAgentMessage"/>s onto its
/// <see cref="Transcript"/>.
/// </para>
/// </summary>
public partial class ChatPageViewModel : ViewModelBase, IDisposable
{
    private readonly ISession? _session;

    /// <summary>Header text — the session's title, or "New Chat" in
    /// pre-session state.</summary>
    [ObservableProperty]
    private string _headerText = "New Chat";

    /// <summary>Transcript of chat lines projected from the live
    /// <see cref="ISession.State"/>. Empty when no session is bound.</summary>
    public ObservableCollection<ViewModelBase> Transcript { get; } = new();

    public PromptInputViewModel PromptInput { get; }

    public ChatPageViewModel(ProviderManager providers, ISession? session)
    {
        ArgumentNullException.ThrowIfNull(providers);
        Providers = providers;
        _session = session;

        PromptInput = new PromptInputViewModel(providers);
        PromptInput.SubmitCallback = HandleSubmit;

        if (session is not null)
            AttachSession(session);
    }

    /// <summary>Provider manager (kept so future phase-wiring can
    /// surface the active model on the header).</summary>
    public ProviderManager Providers { get; }

    private void AttachSession(ISession session)
    {
        // Initial projection + subscribe for subsequent changes. The
        // handler clears and re-projects every StateChanged — the live
        // session message count is bounded (turns + tool results) so a
        // full rebuild is fine. A future optimisation switches to a
        // DIFF renderer that emits only the new tail.
        HeaderText = string.IsNullOrEmpty(session.State.SessionTitle)
            ? "New Chat"
            : session.State.SessionTitle;
        ProjectMessages(session.State.Messages);

        // Workspace is now fixed (NewSessionAsync picked the cwd).
        PromptInput.IsWorkspaceLocked = true;

        session.StateChanged += OnSessionStateChanged;
    }

    private void OnSessionStateChanged(SessionState state)
    {
        HeaderText = string.IsNullOrEmpty(state.SessionTitle)
            ? "New Chat"
            : state.SessionTitle;
        ProjectMessages(state.Messages);

        // IsRunning on the prompt input mirrors the session's running
        // flag so the send button icon (ArrowUp / Stop) and tooltip
        // flip accordingly. Partial OnIsRunningChanged re-evaluates
        // SendIconKind / SendToolTip.
        PromptInput.IsRunning = state.IsRunning;
    }

    private void ProjectMessages(IReadOnlyList<IAgentMessage> messages)
    {
        Transcript.Clear();
        foreach (var msg in messages)
        {
            var line = ProjectMessage(msg);
            if (line is not null)
                Transcript.Add(line);
        }
        // Fire ReadyForDisplay AFTER the ItemsControl has the new
        // collection. The shell's code-behind listens and calls
        // ScrollViewer.ScrollToEnd so the user lands on the latest
        // message rather than the top of a long session. Listeners
        // should defer to the dispatcher thread before actually
        // scrolling — the ItemsControl needs a layout pass to know
        // the new content's extent before ScrollToEnd can land on
        // the bottom.
        ReadyForDisplay?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raised after every <see cref="ProjectMessages"/> pass (initial
    /// attach + every <see cref="ISession.StateChanged"/>). The shell
    /// subscribes so it can scroll the transcript ScrollViewer to the
    /// bottom — without this hook a long session would open at the top
    /// and the user would have to scroll to find the latest exchange.
    /// </summary>
    public event EventHandler? ReadyForDisplay;

    /// <summary>
    /// Project one <see cref="IAgentMessage"/> onto a chat-line VM.
    /// MVP covers the four message types the UI prototype currently
    /// renders: user bubble (UserMessage), assistant markdown
    /// (AssistantMessage — text only, thinking + tool calls deferred),
    /// Bash output (BashExecutionMessage → ToolCard), and tool result
    /// (ToolResultMessage → ToolCard). Other types
    /// (BranchSummaryMessage, CustomMessage) are skipped — a follow-up
    /// adds dedicated line views.
    /// </summary>
    private static ViewModelBase? ProjectMessage(IAgentMessage message)
    {
        return message switch
        {
            UserMessage u => new UserTextLineViewModel(u.Text),

            AssistantMessage a => new AssistantTextLineViewModel(a.Text),

            BashExecutionMessage b => new ToolCardLineViewModel(
                toolName: "Bash",
                summary: b.Command,
                body: b.Output,
                sourcePath: null),

            ToolResultMessage t => new ToolCardLineViewModel(
                toolName: string.IsNullOrEmpty(t.ToolName) ? "Tool" : t.ToolName,
                summary: $"tool result · {t.ToolCallId}",
                body: t.Text,
                sourcePath: null),

            _ => null,
        };
    }

    /// <summary>Wire from <see cref="PromptInputViewModel.SubmitCallback"/>:
    /// always SubmitPrompt on the live session. Today the live session
    /// exists from app start (Composition.InitializeAsync preloads it);
    /// a UI-3 follow-up handles the pre-session state by calling
    /// <c>Composition.NewSessionAsync</c> first and then submitting.</summary>
    private void HandleSubmit(string text, string cwd, string model)
    {
        if (_session is null) return;
        if (string.IsNullOrWhiteSpace(text)) return;
        _session.SubmitPrompt(text);
        // Optimistic UI clear — the new user bubble arrives in the next
        // StateChanged once the harness appends it. If it doesn't (e.g.
        // the harness pre-emptively rejected the submit), the text
        // stays in the editor and the user can retry.
        PromptInput.Text = string.Empty;
    }

    public void Dispose()
    {
        if (_session is not null)
            _session.StateChanged -= OnSessionStateChanged;
    }
}