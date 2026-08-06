using System.Diagnostics.CodeAnalysis;
using PhiAgent;
using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Sessions;
using PhiCoding.Tui;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Pages;

/// <summary>
/// Base for the chat screens: the new-session landing page and the session
/// detail page. Holds everything the two share — the prompt editor, the
/// suggestion strip, slash-command dispatch, the <c>/sessions</c> /
/// <c>/connect</c> / <c>/models</c> dialogs, skill completion, and
/// navigation — and leaves each page to decide its own layout and what to do
/// when a prompt is submitted.
/// <para>
/// Data flow: the session is already hydrated by the navigator (jsonl loaded
/// into <see cref="ISession.State"/> by the factory); the page renders it and
/// subscribes to the session's reactive events. A fresh page instance is built
/// per navigation, so every binding and closure captures exactly the session
/// this page renders.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1051", Justification = "Protected readonly DI fields for derived pages; properties would add ceremony")]
public abstract partial class ChatScreen : IPage
{
    protected readonly ISession _session;
    protected readonly ISessionNavigator _navigator;
    protected readonly ProviderManager _providers;
    private SkillSuggestionProvider? _skillProvider;

    protected ChatScreen(
        ISession session, ISessionNavigator navigator, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        _session = session;
        _navigator = navigator;
        _providers = providers;
    }

    /// <summary>The prompt editor rendered by this page (set by <see cref="Build"/>).</summary>
    public PromptEditor Editor { get; private set; } = null!;

    /// <summary>The suggestion strip rendered by this page (set by <see cref="Build"/>).</summary>
    public SuggestionStrip SuggestionStrip { get; private set; } = null!;

    /// <summary>The page's layout; called at the end of <see cref="Build"/>.</summary>
    protected abstract Visual BuildLayout();

    /// <summary>Surfaces an informational line (dialog feedback, errors).</summary>
    protected abstract void ShowInfo(string message);

    /// <summary>
    /// Called after any prompt (typed text or a skill) is submitted to the
    /// session. The session page renders the user bubble in its transcript;
    /// the new-session page promotes to the session's detail route.
    /// </summary>
    protected virtual void OnSubmitted(string text, bool isSkill) { }

    /// <summary>Surfaces a steering-queued message (session page only).</summary>
    protected virtual void ShowSteeringQueued(string text) { }

    /// <summary>
    /// Builds the page: constructs the view state (<c>State&lt;T&gt;</c>
    /// locals, the React <c>useState</c> analog), assembles the shared editor
    /// and suggestion strip, wires the interactions, and returns the page's
    /// layout.
    /// </summary>
    public Visual Build()
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

        return BuildLayout();
    }

    /// <summary>The shared header: phi logo + the session's provider/model.</summary>
    protected Visual BuildHeader()
    {
        var modelMarkup = new Markup(
            $"[dim]{FormatModel(_session.State.ProviderName, _session.State.Model)}[/]")
        {
            Wrap = false,
        };
        _session.StateChanged += s =>
            modelMarkup.Text = $"[dim]{FormatModel(s.ProviderName, s.Model)}[/]";

        return new Header
        {
            Left = new Markup("[bold]phi[/]") { Wrap = false },
            Right = modelMarkup,
        };
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

    protected void SubmitPrompt(string text)
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

    // ──────── /new ────────

    /// <summary>
    /// Navigates to a fresh session (the landing page). The navigator keeps
    /// the current provider/model for the new session.
    /// </summary>
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

    /// <summary>
    /// Navigates to an indexed session (<c>/sessions/:id</c>). An unknown id
    /// surfaces an info line instead of disturbing the current session.
    /// </summary>
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

    internal static string FormatModel(string providerName, string model) =>
        providerName.Length > 0 ? $"{providerName}/{model}" : model;
}
