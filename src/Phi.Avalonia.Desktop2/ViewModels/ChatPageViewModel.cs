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
    private ISession? _session;
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

        PromptInput = new PromptInputViewModel(
            providers,
            WorkspaceSessionStore.ListWorkspaces());
        PromptInput.SubmitCallback = HandleSubmit;

        if (session is not null)
            AttachSession(session);
    }

    /// <summary>Provider manager (kept so future phase-wiring can
    /// surface the active model on the header).</summary>
    public ProviderManager Providers { get; }
    /// <summary>True when no <see cref="ISession"/> is bound. The shell
    /// uses this to decide whether to rebuild the chat page when
    /// <see cref="ActiveSession.Changed"/> fires after
    /// <c>Composition.NewSessionAsync</c> in pre-session mode — if the
    /// current chat page is pre-session, the shell hands the new
    /// session to it via <see cref="AttachSession"/> instead of
    /// building a fresh VM (which would discard the pending prompt
    /// input the user just submitted).</summary>
    public bool IsPreSession => _session is null;

    /// <summary>Bind the chat page to a live session. Called from the
    /// constructor when the shell already has a session, and from
    /// <see cref="Phi.Avalonia.Desktop2.Composition.NewSessionAsync"/>
    /// path via the shell's <c>OnActiveSessionChanged</c> handler in
    /// pre-session mode. Idempotent for the same session instance —
    /// the StateChanged subscription is only added once.</summary>
    public void AttachSession(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;

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

        };
    }

    /// <summary>Wire from <see cref="PromptInputViewModel.SubmitCallback"/>:
    /// forwards the (text, cwd, model) to the live session. In
    /// pre-session state (no session bound yet — user just hit New
    /// Chat and is in the picker UI) we resolve model → provider and
    /// call <c>Composition.NewSessionAsync</c> to materialise one,
    /// then attach it to ourselves and submit. That
    /// <c>NewSessionAsync</c> internally calls <c>Active.Replace</c>
    /// which fires <see cref="ActiveSession.Changed"/>; the shell's
    /// <c>OnActiveSessionChanged</c> sees the current chat page is
    /// pre-session and routes the new session to our existing
    /// <see cref="AttachSession"/> instead of rebuilding a new VM
    /// (rebuilding would drop the pending text + workspace / model
    /// selection the user just made).</summary>
    private async void HandleSubmit(string text, string cwd, string model)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (_session is null)
        {
            // Pre-session: materialise a session from the picker state.
            var provider = ResolveProviderForModel(model);
            if (string.IsNullOrEmpty(provider)) return;
            var next = await Composition.NewSessionAsync(cwd, provider, model);
            // next == Active.Current now (NewSessionAsync did the swap).
            // The shell's OnActiveSessionChanged has already called
            // AttachSession(next) on us because we were pre-session;
            // just submit.
            next.SubmitPrompt(text);
        }
        else
        {
            _session.SubmitPrompt(text);
        }
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

    /// <summary>Reverse-lookup a model name to the provider that offers
    /// it. The prompt input exposes model names from every connected
    /// provider's catalog (see <see cref="PromptInputViewModel.AvailableModels"/>),
    /// but <c>Composition.NewSessionAsync</c> needs the provider name,
    /// not the model. Returns the first provider whose
    /// <see cref="ProviderCatalogEntry.Models"/> contains
    /// <paramref name="model"/>; empty string if none (the picker
    /// placeholder already covers the no-providers case).</summary>
    private static string ResolveProviderForModel(string model)
    {
        if (string.IsNullOrEmpty(model)) return string.Empty;
        foreach (var entry in ProviderCatalog.All)
        {
            if (entry.Models.Contains(model))
                return entry.Name;
        }
        return string.Empty;
    }
}