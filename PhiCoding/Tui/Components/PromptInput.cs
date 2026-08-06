using PhiAgent;
using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Sessions;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui.Components;

/// <summary>Fired after a prompt (typed text or a skill) is submitted.</summary>
public delegate void PromptSubmittedHandler(string text, bool isSkill);

/// <summary>
/// The shared input shell the chat pages compose. Owns the prompt editor,
/// suggestion strip, slash-command dispatch, skill completion, the
/// <c>/sessions</c> / <c>/connect</c> / <c>/models</c> dialogs, and the
/// provider / model switchers — everything two chat screens would otherwise
/// have to copy. A plain component, not a <see cref="Visual"/>: it composes
/// an existing <see cref="PromptEditor"/>, it doesn't subclass or replace it.
/// <para>
/// The composing page injects three callbacks (<see cref="OnSubmitted"/>,
/// <see cref="ShowInfo"/>, <see cref="ShowSteeringQueued"/>) so the input
/// stays ignorant of the page's layout / transcript / promotion strategy.
/// </para>
/// </summary>
public sealed partial class PromptInput
{
    private readonly ISession _session;
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;

    /// <summary>The session this input is bound to.</summary>
    public ISession Session => _session;

    /// <summary>The navigator this input is bound to (exposed for pages that
    /// need to navigate, e.g. the new-session page promoting to a detail route).</summary>
    public ISessionNavigator Navigator => _navigator;

    /// <summary>The prompt editor constructed by <see cref="Build"/>.</summary>
    public PromptEditor Editor { get; private set; } = null!;

    /// <summary>The live-autocomplete strip constructed by <see cref="Build"/>.</summary>
    public SuggestionStrip SuggestionStrip { get; private set; } = null!;

    /// <summary>(text, isSkill) — fired after a prompt or skill is submitted.</summary>
    public PromptSubmittedHandler OnSubmitted { get; }

    /// <summary>Surfaces an informational line (dialog feedback, errors).</summary>
    public Action<string> ShowInfo { get; }

    /// <summary>Surfaces a steering-queued message (called instead of SubmitPrompt when the session is running).</summary>
    public Action<string> ShowSteeringQueued { get; }

    private SkillSuggestionProvider? _skillProvider;

    public PromptInput(
        ISession session,
        ISessionNavigator navigator,
        ProviderManager providers,
        PromptSubmittedHandler onSubmitted,
        Action<string> showInfo,
        Action<string> showSteeringQueued)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(onSubmitted);
        ArgumentNullException.ThrowIfNull(showInfo);
        ArgumentNullException.ThrowIfNull(showSteeringQueued);
        _session = session;
        _navigator = navigator;
        _providers = providers;
        OnSubmitted = onSubmitted;
        ShowInfo = showInfo;
        ShowSteeringQueued = showSteeringQueued;
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
        SuggestionStrip = new SuggestionStrip(inputText,
            [new SlashCommandProvider(), skillProvider]);

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
                case "/exit":
                    Editor.App?.Stop();
                    break;
            }
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
            ShowSteeringQueued(text);
            return;
        }

        SubmitPrompt(text);
    }

    private void SubmitPrompt(string text)
    {
        _session.SubmitPrompt(text);
        OnSubmitted(text, isSkill: false);
    }

    /// <summary>
    /// Loads a skill and submits it as the user prompt, then lets the page
    /// surface it (transcript bubble / promotion). Unknown skills surface an
    /// info line instead of crashing.
    /// </summary>
    private async Task LoadSkillAsync(string name, string? prompt)
    {
        try
        {
            var content = await _session.LoadSkillAsync(name, prompt);
            OnSubmitted(content, isSkill: true);
        }
        catch (InvalidOperationException ex)
        {
            ShowInfo(ex.Message);
        }
    }

    /// <summary>
    /// Returns and clears the navigator's pending submission, if any. Pages
    /// building a promoted detail view call this so the user bubble can be
    /// rendered when the run is already in flight.
    /// </summary>
    public string? TakePendingSubmission() => _navigator.TakePendingSubmission();

    // ──────── Navigation ────────

    /// <summary>Navigates to a fresh session (the landing page).</summary>
    private async Task NavigateToNewAsync()
    {
        try
        {
            await _navigator.NavigateAsync(new ChatRoute(new NewSessionRequest()));
        }
        catch (Exception ex)
        {
            ShowInfo($"Failed to start new session: {ex.Message}");
        }
    }

    /// <summary>Navigates to an indexed session (<c>/sessions/:id</c>).</summary>
    private async Task NavigateToSessionAsync(string sessionId)
    {
        try
        {
            await _navigator.NavigateAsync(new ChatRoute(new ExistingSessionRequest(sessionId)));
        }
        catch (InvalidOperationException ex)
        {
            ShowInfo(ex.Message);
        }
    }

    // ──────── Slash completion ────────

    private PromptEditorCompletion CompleteSlashCommand(in PromptEditorCompletionRequest request)
    {
        var snapshot = request.Snapshot;
        var caret = Math.Clamp(request.CaretIndex, 0, snapshot.Length);
        var text = string.Create(snapshot.Length, snapshot, static (span, s) => s.CopyTo(0, span));

        // Same tokenizer/filter as the suggestion strip, so Tab completion and
        // the live strip always agree.
        var match = new SlashCommandProvider().GetSuggestion(text, caret)
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
}