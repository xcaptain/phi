using Phi.Agent;
using Phi.Extensions;
using Phi.Providers;
using Phi.Prompt;
using Phi.Slash;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Phi.Tui.Components;

/// <summary>
/// The input shell the chat screen composes. Owns the prompt editor,
/// suggestion strip, slash-command dispatch, skill completion, the
/// <c>/sessions</c> / <c>/connect</c> / <c>/models</c> dialogs, and the
/// provider / model switchers. A plain component, not a <see cref="Visual"/>:
/// it composes an existing <see cref="PromptEditor"/>, it doesn't subclass
/// or replace it.
/// <para>
/// Transient input-status messages (dialog feedback, queued steering) and
/// submitted user prompts are written to the shared <see cref="ChatTranscript"/>.
/// The input owns all of its visible feedback.
/// </para>
/// <para>
/// <c>/new</c> and <c>/sessions</c> navigate via
/// <see cref="ISession.NewSessionAsync"/> / <see cref="ISession.ResumeAsync"/>:
/// the new session is returned, the old one disposes itself, and
/// <see cref="SessionReplaced"/> fires so the shell can re-bind its
/// <c>State&lt;ISession&gt;</c> and the chat page rebuilds against the
/// replacement.
/// </para>
/// </summary>
public sealed partial class PromptInput
{
    private readonly ISession _session;
    private readonly ProviderManager _providers;
    private readonly ChatTranscript _transcript;

    /// <summary>The session this input is bound to.</summary>
    public ISession Session => _session;

    /// <summary>
    /// Fired after a successful <c>/new</c> or <c>/sessions</c> navigation:
    /// the new session is the argument; the old one has already disposed
    /// itself. The shell listens and flips its <c>State&lt;ISession&gt;</c>
    /// so the chat page rebuilds.
    /// </summary>
    public event Action<ISession>? SessionReplaced;

    /// <summary>The prompt editor constructed by <see cref="Build"/>.</summary>
    public PromptEditor Editor { get; private set; } = null!;

    /// <summary>The live-autocomplete strip constructed by <see cref="Build"/>.</summary>
    public SuggestionStrip SuggestionStrip { get; private set; } = null!;

    private SkillSuggestionProvider? _skillProvider;

    private readonly IReadOnlyList<Phi.Slash.SlashCommandDef>? _commands;

    /// <summary>
    /// Extension-registered slash command dispatcher. Called by
    /// <see cref="HandleInput"/> after the built-in switch misses;
    /// <c>null</c> in hosts with no extension runtime (test fakes, headless
    /// runs). The closure has already captured the live
    /// <see cref="IPhiContext"/> from the composition root, so the
    /// callback only needs (name, args) — the handler receives the context
    /// through the closure, not the call site. Returns the transient
    /// message the handler wants shown, or <c>null</c> for silent success.
    /// </summary>
    private readonly Func<string, string, string?>? _dispatcher;

    public PromptInput(
        ISession session,
        ProviderManager providers,
        ChatTranscript transcript,
        IReadOnlyList<Phi.Slash.SlashCommandDef>? commands = null,
        Func<string, string, string?>? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(transcript);
        _session = session;
        _providers = providers;
        _transcript = transcript;
        _commands = commands;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Builds the input: constructs the prompt editor and suggestion strip,
    /// wires the editor's Accepted event to the slash / skill / submit
    /// dispatch, the Canceled event to <c>session.Cancel</c>. Must be called
    /// exactly once before exposing <see cref="Editor"/> or
    /// <see cref="SuggestionStrip"/>.
    /// </summary>
    public void Build()
    {
        var inputText = new State<string?>(string.Empty);

        var skillProvider = new SkillSuggestionProvider(_session.Skills);
        _skillProvider = skillProvider;
        // Built-in commands + anything the extension runtime registered.
        // Null when no extensions are loaded — provider still works against
        // its default catalog in that case.
        SuggestionStrip = new SuggestionStrip(inputText,
            [new SlashCommandProvider(_commands), skillProvider]);

        Editor = new PromptEditor()
            .Prompt(new Markup("[primary]❯[/] "))
            .ContinuationPromptMarkup("[dim]·[/]")
            .Text(inputText)
            .Placeholder("Ask Phi anything… (Enter submit · Esc cancel · Ctrl+Q quit)")
            .CompletionPresentation(PromptEditorCompletionPresentation.PopupList)
            .CompletionHandler(CompleteSlashCommand)
            .MinHeight(3)
            .MaxHeight(10)
            .AutoFocus(true);

        Editor.Accepted((_, e) =>
        {
            var text = e.Text.Trim();
            inputText.Value = string.Empty;
            if (text.Length == 0) return;
            HandleInput(text);
        });
        Editor.Canceled((_, _) => _session.Cancel());
    }

    // ──────── Input dispatch ────────

    private void HandleInput(string text)
    {
        if (SlashCommands.Match(text) is { } command)
        {
            switch (command)
            {
                case "/new":
                    _ = NavigateToNewAsync();
                    break;
                case "/sessions":
                    ShowSessionsDialog();
                    break;
                case "/connect":
                    ShowConnectDialog();
                    break;
                case "/models":
                    ShowModelsDialog();
                    break;
                case "/reload":
                    ReloadExtensions();
                    break;
                case "/exit":
                    Editor.App?.Stop();
                    break;
            }
            return;
        }

        // Built-in switch missed. Fall through to the extension registry.
        // Extension commands aren't in SlashCommandCatalog (which is
        // static), so we re-derive the (name, args) split here against the
        // merged command list PhiTuiApp passed in. The dispatcher swallows
        // its own handler exceptions and surfaces them as a transient
        // (see ExtensionRuntime.TryDispatch), so we only need to handle the
        // "unknown name" case by falling through to the regular prompt path.
        if (_dispatcher is not null && _commands is not null
            && TrySplitExtensionCommand(text, out var cmdName, out var cmdArgs)
            && _commands.Any(c => c.Name.TrimStart('/').Equals(
                cmdName, StringComparison.OrdinalIgnoreCase)))
        {
            // The dispatcher closure already captured the live IPhiContext
            // from the composition root; it returns null when the registry
            // doesn't recognise the command (so the caller falls through to
            // submitting-as-prompt). Handler failures are swallowed upstream
            // and surface as a non-null error message.
            var msg = _dispatcher(cmdName, cmdArgs);
            if (msg is not null) _transcript.ShowTransient(msg);
            return;
        }

        if (SlashCommands.MatchSkill(text) is { } skillMatch)
        {
            _ = LoadSkillAsync(skillMatch.SkillName, skillMatch.Prompt);
            return;
        }

        if (SlashCommands.MatchWithArgs(text) is { } withArgs)
        {
            switch (withArgs.Command)
            {
                case "/connect":
                    ConnectProviderByName(withArgs.Args);
                    break;
            }
            return;
        }

        if (_session.State.IsRunning)
        {
            _session.EnqueueSteering(new UserMessage { Content = text });
            _transcript.ShowTransient($"[queued · steering] {text}");
            return;
        }

        SubmitPrompt(text);
    }

    private void SubmitPrompt(string text)
    {
        _transcript.AddUserMessage(text);
        _session.SubmitPrompt(text);
    }

    /// <summary>
    /// Loads a skill and submits it as the user prompt; the loaded content
    /// is rendered as a user bubble so the submission is visible before the
    /// model's response streams in. Unknown skills surface an info line
    /// instead of crashing.
    /// </summary>
    private async Task LoadSkillAsync(string name, string? prompt)
    {
        try
        {
            var content = await _session.LoadSkillAsync(name, prompt);
            _transcript.AddUserMessage(content);
            _transcript.ResetRenderedCount();
        }
        catch (InvalidOperationException ex)
        {
            _transcript.ShowTransient(ex.Message);
        }
    }

    // ──────── Navigation ────────

    /// <summary>
    /// Navigates to a fresh session via the active session itself
    /// (<see cref="ISession.NewSessionAsync"/>); the new session inherits
    /// the current session's provider and model. The old session disposes
    /// itself; <see cref="SessionReplaced"/> fires for the shell to
    /// re-bind. A failure surfaces as a transient message and leaves the
    /// current session intact.
    /// </summary>
    private async Task NavigateToNewAsync()
    {
        try
        {
            var next = await _session.NewSessionAsync();
            SessionReplaced?.Invoke(next);
        }
        catch (Exception ex)
        {
            _transcript.ShowTransient($"Failed to start new session: {ex.Message}");
        }
    }

    /// <summary>
    /// Resumes an indexed session by id via the active session itself
    /// (<see cref="ISession.ResumeAsync"/>); the session's own cwd is
    /// resolved from the record so cross-workspace resume works. The old
    /// session disposes itself; <see cref="SessionReplaced"/> fires.
    /// Unknown ids surface as a transient and leave the current session
    /// intact.
    /// </summary>
    private async Task ResumeAsync(string sessionId)
    {
        try
        {
            var next = await _session.ResumeAsync(sessionId);
            SessionReplaced?.Invoke(next);
        }
        catch (InvalidOperationException ex)
        {
            _transcript.ShowTransient(ex.Message);
        }
    }

    /// <summary>
    /// Reloads the session's extension set: disposes the current extension
    /// runtime (unloading ALCs, invalidating captured <c>IPhiApi</c>
    /// references, clearing hooks + event dispatch) and asks the composition
    /// root's <see cref="SessionEnvironment.ExtensionRuntimeFactory"/> for a
    /// fresh one. CodingPack (and any other compiled extension registered
    /// through the factory) re-registers automatically, so the four coding
    /// tools stay in the harness. Failures surface as a transient message;
    /// the session stays usable (re-call /reload).
    /// </summary>
    private void ReloadExtensions()
    {
        try
        {
            _session.ReloadExtensions();
            _transcript.ShowTransient("Extensions reloaded.");
        }
        catch (Exception ex)
        {
            _transcript.ShowTransient($"Reload failed: {ex.Message}");
        }
    }

    // ──────── Slash completion ────────

    private PromptEditorCompletion CompleteSlashCommand(in PromptEditorCompletionRequest request)
    {
        var snapshot = request.Snapshot;
        var caret = Math.Clamp(request.CaretIndex, 0, snapshot.Length);
        var text = string.Create(snapshot.Length, snapshot, static (span, s) => s.CopyTo(0, span));

        // Same tokenizer/filter as the suggestion strip, so Tab completion and
        // the live strip always agree. The merged list includes both built-ins
        // and any extension-registered commands.
        var match = new SlashCommandProvider(_commands).GetSuggestion(text, caret)
            ?? _skillProvider!.GetSuggestion(text, caret);
        if (match is null)
            return new PromptEditorCompletion(false, null, 0, 0);

        List<string> candidates = [.. match.Items.Select(i => i.Replacement)];
        var prefixLength = caret - match.ReplaceStart;

        string? ghost = null;
        if (caret == text.Length && candidates[0].Length > prefixLength)
            ghost = candidates[0][prefixLength..];

        return new PromptEditorCompletion(true, candidates, match.ReplaceStart, prefixLength, 0, ghost);
    }

    /// <summary>
    /// Splits a slash input into (canonicalName, trailingArgs). The name is
    /// the first whitespace-separated token with any leading <c>/</c> stripped;
    /// the args are everything after the first whitespace (trimmed). Returns
    /// false for inputs that don't start with <c>/</c> or are empty after
    /// the trim — those are user prompts, not commands, and the caller
    /// should fall back to the regular submit path.
    /// </summary>
    private static bool TrySplitExtensionCommand(
        string text, out string name, out string args)
    {
        name = "";
        args = "";
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/') return false;

        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0)
        {
            name = trimmed.TrimStart('/');
            return name.Length > 0;
        }
        name = trimmed[..firstSpace].TrimStart('/');
        if (name.Length == 0) return false;
        args = trimmed[(firstSpace + 1)..].Trim();
        return true;
    }
}
