using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using PhiAgent;
using PhiCoding.Chat;
using PhiCoding.Prompt;
using PhiCoding.Providers;
using PhiCoding.Sessions;
using PhiCoding.Slash;

namespace PhiCoding.Desk.Components;

/// <summary>
/// The prompt input shell. Composes a <see cref="MultiLineTextBox"/> with
/// a slash/skill completion hint. <c>Enter</c> submits (Shift+Enter inserts
/// a newline), <c>Esc</c> cancels the running turn. Slash commands
/// (<c>/new</c>, <c>/exit</c>) dispatch through <see cref="HandleInput"/>;
/// skill invocations (<c>/skill:NAME</c>) load the skill into the
/// conversation; everything else becomes a user message (or, if a run is in
/// flight, is queued as steering). Submitting always writes the user's own
/// message into the projector so it appears in the transcript immediately —
/// the session only exposes new messages at <c>TurnEndEvent</c>.
/// </summary>
public sealed class PromptInputView
{
    private readonly ISession _session;
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;
    private readonly ChatTranscriptProjector _projector;

    private readonly ObservableValue<string> _text = new(string.Empty);
    private readonly ObservableValue<string> _completionText = new(string.Empty);
    private readonly ObservableValue<bool> _completionVisible = new(false);

    private readonly IReadOnlyList<ISuggestionProvider> _suggestionProviders;

    public PromptInputView(
        ISession session,
        ISessionNavigator navigator,
        ProviderManager providers,
        ChatTranscriptProjector projector)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(projector);

        _session = session;
        _navigator = navigator;
        _providers = providers;
        _projector = projector;
        _suggestionProviders = [new SlashCommandProvider(), new SkillSuggestionProvider(_session.Skills)];
    }

    public FrameworkElement Root { get; private set; } = null!;

    /// <summary>The editor's bound text observable (tests).</summary>
    internal ObservableValue<string> Text => _text;

    /// <summary>Drives the input dispatch directly (tests). Reads the current
    /// editor text from <see cref="Text"/>.</summary>
    internal void SubmitForTest() => SubmitCurrent();

    /// <summary>
    /// Builds the input shell: constructs the editor and completion hint,
    /// wires the editor's key events to the slash / skill / submit
    /// dispatch. Must be called exactly once before <see cref="Root"/> is
    /// accessed.
    /// </summary>
    public void Build()
    {
        var editor = new MultiLineTextBox()
            .BindText(_text)
            .Placeholder("Ask Phi anything… (Ctrl+Enter submit · Esc cancel)")
            .Wrap(true)
            .FontFamily("Consolas")
            .MinHeight(48)
            .MaxHeight(200);

        // Recompute completion on every text change.
        _text.Subscribe(UpdateCompletion);

        var submitButton = new Button()
            .Content("Submit")
            .OnClick(SubmitCurrent);

        editor.KeyDown += e =>
        {
            // Enter submits (matching the TUI); Shift+Enter inserts a newline.
            // Setting Handled suppresses the editor's own newline insertion.
            if (e.Key == Key.Enter && (e.Modifiers & ModifierKeys.Shift) == 0)
            {
                SubmitCurrent();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _session.Cancel();
                e.Handled = true;
            }
        };

        var completionHint = new Label()
            .BindText(_completionText)
            .BindIsVisible(_completionVisible)
            .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)))
            .TextWrapping(TextWrapping.NoWrap);

        var inputColumn = new StackPanel()
            .Orientation(Aprillz.MewUI.Orientation.Vertical)
            .Spacing(4)
            .Children(editor, completionHint);

        var inputRow = new DockPanel()
            .LastChildFill()
            .Padding(8, 6)
            .Children(
                submitButton.DockRight(),
                new Border().Padding(0, 0, 8, 0).Child(inputColumn));

        Root = inputRow;
    }

    private void UpdateCompletion()
    {
        var text = _text.Value;
        var caret = text.Length;
        foreach (var provider in _suggestionProviders)
        {
            if (provider.GetSuggestion(text, caret) is { } match && match.Items.Count > 0)
            {
                _completionText.Value = $"↳ {match.Items[0].Label} — {match.Items[0].Description}";
                _completionVisible.Value = true;
                return;
            }
        }
        _completionVisible.Value = false;
    }

    private void SubmitCurrent()
    {
        var text = _text.Value.Trim();
        _text.Value = string.Empty;
        if (text.Length == 0) return;
        HandleInput(text);
    }

    private void HandleInput(string text)
    {
        if (SlashCommands.Match(text) is { } command)
        {
            switch (command)
            {
                case "/new":
                    _ = _navigator.NavigateToNewAsync();
                    break;
                case "/exit":
                    Application.Quit();
                    break;
                case "/sessions":
                case "/connect":
                case "/models":
                    // Desktop exposes these through menu actions, not the
                    // input shell; silently ignore to keep the input
                    // shell UI-agnostic.
                    break;
            }
            return;
        }

        if (SlashCommands.MatchSkill(text) is { } skillMatch)
        {
            _ = LoadSkillAsync(skillMatch.SkillName, skillMatch.Prompt);
            return;
        }

        if (_session.State.IsRunning)
        {
            // Reflect the queued steering message in the transcript too, so
            // the user sees their input even while a turn is in flight.
            _projector.SubmitUserLine(text);
            _session.EnqueueSteering(new UserMessage { Content = text });
            return;
        }

        _projector.SubmitUserLine(text);
        _session.SubmitPrompt(text);
    }

    private async Task LoadSkillAsync(string name, string? prompt)
    {
        try
        {
            var content = await _session.LoadSkillAsync(name, prompt);
            // The loaded skill body rides along as the user message; render
            // it (as a collapsible skill card) via the projector.
            _projector.SubmitUserLine(content);
        }
        catch (InvalidOperationException)
        {
            // Skill load failed (unknown name, run in progress). The
            // transcript surfaces errors via the status router; we just
            // suppress here so the input flow continues.
        }
    }
}
