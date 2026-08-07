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
    private readonly Func<string?> _pickFolder;
    private readonly Action<Action> _postToUi;

    private readonly ObservableValue<string> _text = new(string.Empty);
    private readonly ObservableValue<string> _completionText = new(string.Empty);
    private readonly ObservableValue<bool> _completionVisible = new(false);
    private readonly ObservableValue<bool> _workspacePickerVisible = new(false);

    private MultiLineTextBox? _editor;

    private readonly IReadOnlyList<ISuggestionProvider> _suggestionProviders;

    public PromptInputView(
        ISession session,
        ISessionNavigator navigator,
        ProviderManager providers,
        ChatTranscriptProjector projector,
        Func<string?>? pickFolder = null,
        Action<Action>? postToUi = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(projector);

        _session = session;
        _navigator = navigator;
        _providers = providers;
        _projector = projector;
        _pickFolder = pickFolder ?? (() => null);
        _postToUi = postToUi ?? (action => action());
        _suggestionProviders = [new SlashCommandProvider(), new SkillSuggestionProvider(_session.Skills)];
    }

    public FrameworkElement Root { get; private set; } = null!;

    /// <summary>The session this input is bound to (tests).</summary>
    internal ISession Session => _session;

    /// <summary>The prompt editor (tests).</summary>
    internal MultiLineTextBox Editor => _editor!;

    /// <summary>The editor's bound text observable (tests).</summary>
    internal ObservableValue<string> Text => _text;

    /// <summary>Whether the workspace picker is shown (tests).</summary>
    internal bool WorkspacePickerVisible => _workspacePickerVisible.Value;

    /// <summary>The workspace picker ComboBox, when built (tests).</summary>
    internal ComboBox? WorkspaceComboBox { get; private set; }

    /// <summary>Drives the input dispatch directly (tests). Reads the current
    /// editor text from <see cref="Text"/>.</summary>
    internal void SubmitForTest() => SubmitCurrent();

    /// <summary>Triggers a workspace switch as a picker selection would (tests).</summary>
    internal void SelectWorkspaceForTest(string cwd) => SwitchWorkspace(cwd);

    /// <summary>Moves keyboard focus to the prompt editor. Called after the
    /// chat page is built/shown so Enter submits instead of acting on
    /// whatever previously had focus (e.g. the workspace picker).</summary>
    public void FocusEditor() => _editor?.Focus();

    /// <summary>
    /// Builds the input shell: constructs the editor and completion hint,
    /// wires the editor's key events to the slash / skill / submit
    /// dispatch. Must be called exactly once before <see cref="Root"/> is
    /// accessed.
    /// </summary>
    public void Build()
    {
        _editor = new MultiLineTextBox()
            .BindText(_text)
            .Placeholder("Ask Phi anything… (Enter submit · Esc cancel)")
            .Wrap(true)
            .FontFamily("Consolas")
            .MinHeight(48)
            .MaxHeight(200);

        // Recompute completion on every text change.
        _text.Subscribe(UpdateCompletion);

        var submitButton = new Button()
            .Content("Submit")
            .OnClick(SubmitCurrent);

        _editor.KeyDown += e =>
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

        // A fresh (unpersisted) session lets the user choose which workspace
        // it belongs to before the first message; once the session has
        // messages the picker disappears (the cwd is committed).
        _workspacePickerVisible.Value = _session.State.Messages.Count == 0;
        _session.StateChanged += OnSessionStateForPicker;
        var workspacePicker = BuildWorkspacePicker();

        var inputColumn = new StackPanel()
            .Orientation(Aprillz.MewUI.Orientation.Vertical)
            .Spacing(4)
            .Children(workspacePicker, _editor, completionHint);

        var inputRow = new DockPanel()
            .LastChildFill()
            .Padding(8, 6)
            .Children(
                submitButton.DockRight(),
                new Border().Padding(0, 0, 8, 0).Child(inputColumn));

        Root = inputRow;
    }

    private void OnSessionStateForPicker(SessionState state)
    {
        if (state.Messages.Count > 0)
            _workspacePickerVisible.Value = false;
    }

    // ──────── Workspace picker (fresh sessions only) ────────

    /// <summary>
    /// Builds the workspace picker row: a ComboBox of the distinct
    /// workspaces derived from session records, plus a separate "Choose
    /// folder…" button that opens the native folder dialog. The dialog is
    /// only ever triggered by a button click — keeping it out of the
    /// ComboBox's selection handling avoids the modal dialog's close from
    /// re-firing <c>SelectionChanged</c> and popping the dialog twice.
    /// Selecting a workspace (or picking a folder) recreates the fresh
    /// session in that directory via
    /// <see cref="ISessionNavigator.NavigateToNewAsync(string?)"/>.
    /// </summary>
    private Border BuildWorkspacePicker()
    {
        var workspaces = WorkspaceSessionStore.ListWorkspaces().ToList();
        // Always keep the session's current cwd selectable/visible, even if
        // it isn't in the record-derived list (e.g. just picked a new folder).
        if (!workspaces.Any(w => Path.GetFullPath(w) == Path.GetFullPath(_session.Cwd)))
            workspaces.Insert(0, _session.Cwd);

        var combo = new ComboBox()
            .Items(workspaces.Select(DeskNavModel.WorkspaceLabel).ToArray())
            .Width(280);
        var currentIndex = workspaces.FindIndex(
            w => Path.GetFullPath(w) == Path.GetFullPath(_session.Cwd));
        combo.SelectedIndex = currentIndex >= 0 ? currentIndex : 0;
        WorkspaceComboBox = combo;

        combo.SelectionChanged += _ =>
        {
            var idx = combo.SelectedIndex;
            if (idx >= 0 && idx < workspaces.Count)
                SwitchWorkspace(workspaces[idx]);
        };

        var chooseFolderButton = new Button()
            .Content("Choose folder…")
            .OnClick(ChooseFolder);

        var row = new DockPanel()
            .LastChildFill()
            .Spacing(8)
            .Children(
                new Label()
                    .Text("Workspace")
                    .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)))
                    .DockLeft()
                    .CenterVertical(),
                chooseFolderButton.DockRight(),
                combo);

        var holder = new Border()
            .Padding(0, 2)
            .Child(row);
        holder.BindIsVisible(_workspacePickerVisible);
        return holder;
    }

    /// <summary>
    /// Switches the fresh session to an existing workspace by navigating to a
    /// new session in that directory. No-op when it already matches.
    /// </summary>
    private void SwitchWorkspace(string cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return;
        if (Path.GetFullPath(cwd) == Path.GetFullPath(_session.Cwd)) return;
        // Defer the navigation out of the picker's SelectionChanged dispatch.
        // Navigating synchronously rebuilds the whole chat page while the
        // ComboBox event is still settling — re-entering the visual tree and
        // leaving the editor unresponsive to submit afterwards.
        _postToUi(() => _ = _navigator.NavigateToNewAsync(cwd));
    }

    private bool _pickingFolder;

    private void ChooseFolder()
    {
        // Re-entrancy guard: the native modal dialog's open/close can deliver
        // a stray second activation; only ever show it once per click.
        if (_pickingFolder) return;
        _pickingFolder = true;
        try
        {
            var picked = _pickFolder();
            if (string.IsNullOrEmpty(picked)) return;
            _postToUi(() => _ = _navigator.NavigateToNewAsync(picked));
        }
        finally
        {
            _pickingFolder = false;
        }
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
        // Fallback: if the editor's typed text didn't propagate to the bound
        // observable (e.g. after a page rebuild), read it straight from the
        // editor so submit still works.
        if (text.Length == 0 && _editor is not null)
            text = _editor.Text.Trim();
        DeskLog.Write($"SubmitCurrent: text='{text}' len={text.Length} cwd='{_session.Cwd}' running={_session.State.IsRunning}");
        _text.Value = string.Empty;
        if (_editor is not null) _editor.Text = string.Empty;
        if (text.Length == 0) return;
        HandleInput(text);
    }

    private void HandleInput(string text)
    {
        if (SlashCommands.Match(text) is { } command)
        {
            DeskLog.Write($"HandleInput: slash command '{command}'");
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
            DeskLog.Write($"HandleInput: skill '{skillMatch.SkillName}'");
            _ = LoadSkillAsync(skillMatch.SkillName, skillMatch.Prompt);
            return;
        }

        if (_session.State.IsRunning)
        {
            DeskLog.Write("HandleInput: steering (running)");
            // Reflect the queued steering message in the transcript too, so
            // the user sees their input even while a turn is in flight.
            _projector.SubmitUserLine(text);
            _session.EnqueueSteering(new UserMessage { Content = text });
            return;
        }

        DeskLog.Write("HandleInput: SubmitPrompt");
        try
        {
            _projector.SubmitUserLine(text);
            _session.SubmitPrompt(text);
        }
        catch (Exception ex)
        {
            DeskLog.Write($"HandleInput: SubmitPrompt threw: {ex}");
        }
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
