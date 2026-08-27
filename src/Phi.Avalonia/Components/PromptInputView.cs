using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Material.Icons;
using Phi.Agent;
using Phi.Chat;
using Phi.Extensions;
using Phi.Extensions.Host;
using Phi.Prompt;
using Phi.Providers;
using Phi.Slash;

namespace Phi.Avalonia.Components;

/// <summary>
/// One row in the slash-command auto-complete popup. Carries the
/// full command name (which replaces the current <c>/</c> token on
/// accept) plus the description surfaced as a tooltip / secondary
/// text in the list row.
/// </summary>
public sealed record SlashAutoCompleteItem(string Replacement, string Description);

/// <summary>
/// The prompt input controller. Owns the input's behaviour — dispatching
/// editor text via the shared <see cref="SlashInputDispatcher"/> (so
/// <c>/new</c>, <c>/skill:NAME</c>, <c>/reload</c>, extension-registered
/// commands, and ordinary submit / steering all behave identically to the
/// TUI), the model picker (every provider's models grouped by provider
/// with the current one marked), the workspace picker (fresh sessions
/// only), and the submit button's idle/running glyph — and wires it onto
/// the named controls of <see cref="PromptInputLayout"/>.
/// <para>
/// Dialog-only slash commands (<c>/sessions</c>, <c>/connect</c>,
/// <c>/models</c>) are intentionally not dialogs here — the desk reaches
/// the same actions via the sidebar's session list and the model picker
/// footer combo. The dispatcher instead surfaces a transient hint via
/// the projector (see <see cref="AvaloniaSlashActionSink"/>). <c>/exit</c>
/// shuts the application lifetime down.
/// </para>
/// <para>
/// Workspace switches (the picker) drive <see cref="ISession.NewSessionAsync"/>
/// directly: the new session is handed to the shared
/// <see cref="ActiveSession"/>, which atomically swaps and notifies the
/// shell to rebuild the chat page. Old session is disposed inside
/// <c>NewSessionAsync</c>.
/// </para>
/// </summary>
public sealed class PromptInputView
{
    private readonly ISession _session;
    private readonly ActiveSession _active;
    private readonly ProviderManager _providers;
    private readonly ChatTranscriptProjector _projector;
    private readonly Func<Task<string?>> _pickFolder;
    private readonly Action<Action> _postToUi;
    private readonly Action<Action> _dispatchToUi;
    private readonly PromptInputLayout _layout;
    private readonly List<SlashCommandDef> _commands;
    private readonly Func<string, string, bool, string?>? _extensionDispatcher;

    private bool _suppressModelSelection;
    private bool _pickingFolder;

    private IReadOnlyList<ModelPickerItem> _modelItems = Array.Empty<ModelPickerItem>();
    private IReadOnlyList<WorkspacePickerItem> _workspaceItems = Array.Empty<WorkspacePickerItem>();

    /// <summary>Commands the controller was wired with (built-ins +
    /// extension-registered). Empty when no runtime is attached.</summary>
    internal IReadOnlyList<SlashCommandDef> Commands => _commands;

    /// <summary>Closure invoked for extension-registered commands (tests).</summary>
    internal Func<string, string, bool, string?>? ExtensionDispatcher => _extensionDispatcher;

    /// <summary>Last extension command dispatched (tests).</summary>
    internal (string Name, string Args)? LastExtensionDispatch { get; private set; }

    public PromptInputView(
        ISession session,
        ActiveSession active,
        ProviderManager providers,
        ChatTranscriptProjector projector,
        IReadOnlyList<SlashCommandDef>? commands = null,
        Func<ISlashCommandRegistry?>? commandRegistryAccessor = null,
        Func<IPhiContext?>? contextAccessor = null,
        Func<Task<string?>>? pickFolder = null,
        Action<Action>? postToUi = null,
        Action<Action>? dispatchToUi = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(projector);

        _session = session;
        _active = active;
        _providers = providers;
        _projector = projector;
        _pickFolder = pickFolder ?? (() => Task.FromResult<string?>(null));
        _postToUi = postToUi ?? Post;
        _dispatchToUi = dispatchToUi ?? Dispatch;

        _commands = ResolveCommands(commandRegistryAccessor);
        _extensionDispatcher = BuildExtensionDispatcher(
            commandRegistryAccessor, contextAccessor);

        _layout = new PromptInputLayout();

        WireEditor();
        WireSubmitButton();
        ConfigureModelCombo();
        ConfigureWorkspaceCombo();
        ConfigureAutoComplete();
    }

    /// <summary>The prompt input layout (the rounded input shell).</summary>
    public Control Root => _layout;

    /// <summary>The session this input is bound to (tests).</summary>
    internal ISession Session => _session;

    /// <summary>The prompt editor (tests).</summary>
    internal TextBox Editor => _layout.Editor;

    /// <summary>Whether the workspace picker is shown (tests).</summary>
    internal bool WorkspacePickerVisible => _layout.WorkspaceCombo.IsVisible;

    /// <summary>The workspace picker ComboBox (tests).</summary>
    internal ComboBox WorkspaceComboBox => _layout.WorkspaceCombo;

    /// <summary>The model picker ComboBox (tests).</summary>
    internal ComboBox ModelComboBox => _layout.ModelCombo;

    /// <summary>The built model picker item list (tests).</summary>
    internal IReadOnlyList<ModelPickerItem> ModelItems => _modelItems;

    /// <summary>The built workspace picker item list (tests).</summary>
    internal IReadOnlyList<WorkspacePickerItem> WorkspaceItems => _workspaceItems;

    /// <summary>Drives the input dispatch directly (tests). Reads the
    /// current editor text.</summary>
    internal void SubmitForTest() => SubmitCurrent();

    /// <summary>The submit button (tests).</summary>
    internal Button SubmitButton => _layout.SubmitButton;

    /// <summary>The submit button's current glyph (tests).</summary>
    internal MaterialIconKind? SubmitIconKind => _layout.SubmitIcon.Kind;

    /// <summary>Triggers a workspace switch as a picker selection would (tests).</summary>
    internal void SelectWorkspaceForTest(string cwd) => SwitchWorkspace(cwd);

    /// <summary>Moves keyboard focus to the prompt editor.</summary>
    public void FocusEditor() => _layout.Editor.Focus();

    // ──────── Slash command wiring ────────

    /// <summary>
    /// Merges the built-in catalog with whatever the supplied registry
    /// exposes; returns the catalog unchanged when the accessor is null
    /// (no extension runtime). The list is mutable so tests can append
    /// extra commands through <see cref="RegisterExtensionCommand"/>.
    /// </summary>
    private static List<SlashCommandDef> ResolveCommands(
        Func<ISlashCommandRegistry?>? commandRegistryAccessor)
    {
        var registry = commandRegistryAccessor?.Invoke();
        var merged = new List<SlashCommandDef>(SlashCommandCatalog.All.Count + 16);
        merged.AddRange(SlashCommandCatalog.All);
        if (registry is not null)
            merged.AddRange(registry.AllCommands);
        return merged;
    }

    /// <summary>
    /// Builds the extension dispatcher closure (null when no registry is
    /// attached). The closure captures the live <see cref="IPhiContext"/>
    /// so registry handlers see the session's metadata via
    /// <c>ctx.Cwd / ctx.Model / etc.</c>; <paramref name="isRunning"/> is
    /// passed through so an extension can choose to steer instead of
    /// submit.
    /// </summary>
    private Func<string, string, bool, string?>? BuildExtensionDispatcher(
        Func<ISlashCommandRegistry?>? commandRegistryAccessor,
        Func<IPhiContext?>? contextAccessor)
    {
        if (commandRegistryAccessor is null || contextAccessor is null) return null;
        return (name, args, isRunning) =>
        {
            LastExtensionDispatch = (name, args);
            var registry = commandRegistryAccessor();
            var context = contextAccessor();
            if (registry is null || context is null) return null;
            return registry.TryDispatch(name, args, context, out var msg) ? msg : null;
        };
    }

    /// <summary>
    /// Registers one extra slash command visible to the autocomplete and
    /// the dispatcher (tests). Refreshes the auto-complete popup if the
    /// editor has any text so the new command shows up without further
    /// typing.
    /// </summary>
    internal void RegisterExtensionCommand(string name, string description = "Extension command.")
    {
        var canonical = name.StartsWith('/') ? name : "/" + name;
        if (_commands.Any(c => c.Name.Equals(canonical, StringComparison.OrdinalIgnoreCase)))
            return;
        _commands.Add(new SlashCommandDef(canonical, description));
        UpdateAutoComplete(_layout.Editor.Text ?? string.Empty,
            _layout.Editor.CaretIndex);
    }

    private void WireEditor()
    {
        _layout.Editor.KeyDown += (_, e) =>
        {
            // Directional keys move the caret / selection (handled by the
            // TextBox itself); mark them Handled so they don't bubble up to
            // the SukiSideMenu, whose TreeView.OnKeyDown navigates its
            // (empty) items source and crashes on arrow keys.
            if (e.Key.ToNavigationDirection()?.IsDirectional() == true)
            {
                e.Handled = true;
                return;
            }

            // Enter submits; Shift+Enter inserts a newline. Marking Handled
            // suppresses the TextBox's own newline insertion.
            if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0)
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
    }

    private void WireSubmitButton()
    {
        _layout.SubmitButton.Click += (_, _) =>
        {
            // While a turn runs, the button becomes a stop control; once
            // idle it submits. Esc in the editor also cancels.
            if (_session.State.IsRunning)
                _session.Cancel();
            else
                SubmitCurrent();
        };
        UpdateSubmitButton(_session.State);
        _session.StateChanged += state => _dispatchToUi(() => UpdateSubmitButton(state));
    }

    // ──────── Model picker ────────

    /// <summary>
    /// Configures the model picker footer combo: its popup lists every
    /// provider's models, grouped by provider with a styled header row
    /// above each group. The current provider + model are listed first and
    /// marked with ✓. Selecting a row constructs the live
    /// <see cref="IPhiProvider"/> and calls
    /// <see cref="ISession.SwitchProvider"/>; header rows are ignored.
    /// </summary>
    private void ConfigureModelCombo()
    {
        var combo = _layout.ModelCombo;
        RebuildModelItems(_session.State.ProviderName, _session.State.Model);

        // ItemTemplate comes from the implicit DataTemplate in
        // PromptInputLayout.axaml (matched by ModelPickerItem's DataType),
        // so no template wiring is needed here — just the data + behaviour.
        combo.ItemsSource = _modelItems;

        // Select the row for the live model, not just the provider's first
        // model (IndexOfFirstSelectable would highlight the wrong row when
        // the current model isn't first in the catalog).
        var currentIndex = _modelItems.IndexOfCurrent() >= 0
            ? _modelItems.IndexOfCurrent()
            : _modelItems.IndexOfFirstSelectable();
        if (currentIndex >= 0)
            combo.SelectedIndex = currentIndex;

        combo.SelectionChanged += (_, _) =>
        {
            if (_suppressModelSelection) return;
            var idx = combo.SelectedIndex;
            if (idx < 0 || idx >= _modelItems.Count) return;
            var item = _modelItems[idx];
            if (item.IsHeader || item.Entry is null || item.Model is null)
            {
                // A header row can't be chosen: snap back to the current row.
                var reset = _modelItems.IndexOfFirstSelectable();
                if (reset >= 0 && reset != idx)
                {
                    _suppressModelSelection = true;
                    try { combo.SelectedIndex = reset; }
                    finally { _suppressModelSelection = false; }
                }
                return;
            }
            ApplyModelSelection(item.Entry, item.Model);
        };

        _session.StateChanged += state => _dispatchToUi(() => OnSessionStateForModel(state));
    }

    private void OnSessionStateForModel(SessionState state)
    {
        RebuildModelItems(state.ProviderName, state.Model);
        var combo = _layout.ModelCombo;
        // Keep the selection on the live model's row (IndexOfFirstSelectable
        // would snap back to the provider's first model).
        var idx = _modelItems.IndexOfCurrent();
        if (idx < 0) idx = _modelItems.IndexOfFirstSelectable();
        if (idx >= 0 && combo.SelectedIndex != idx)
        {
            _suppressModelSelection = true;
            try
            {
                combo.ItemsSource = null;
                combo.ItemsSource = _modelItems;
                combo.SelectedIndex = idx;
            }
            finally { _suppressModelSelection = false; }
        }
    }

    private void RebuildModelItems(string currentProviderName, string currentModel)
    {
        _modelItems = PromptInputPickerBuilder.BuildModelPickerItems(
            ProviderCatalog.All,
            currentProviderName,
            currentModel,
            _providers.HasApiKey);
    }

    private void ApplyModelSelection(ProviderCatalogEntry entry, string model)
    {
        try
        {
            if (!_providers.HasApiKey(entry))
            {
                DeskLog.Write($"ApplyModelSelection: no API key for {entry.Name}; ignoring");
                return;
            }
            var apiKey = _providers.GetApiKey(entry);
            var provider = ProviderManager.CreateProvider(entry, apiKey);
            _session.SwitchProvider(provider, entry.Name, model);
        }
        catch (Exception ex)
        {
            DeskLog.Write($"ApplyModelSelection: threw: {ex}");
        }
    }

    // ──────── Workspace picker ────────

    /// <summary>
    /// Configures the workspace picker footer combo: lists the distinct
    /// workspaces derived from session records plus the session's current
    /// cwd if it isn't already present, with a trailing "📁 Choose folder…"
    /// sentinel row that opens the native folder dialog. Selecting a
    /// workspace (or picking a folder) recreates the fresh session in that
    /// directory via <c>ISession.NewSessionAsync</c>.
    /// </summary>
    private void ConfigureWorkspaceCombo()
    {
        var combo = _layout.WorkspaceCombo;
        RebuildWorkspaceItems(_session.Cwd);

        // ItemTemplate comes from the implicit DataTemplate in
        // PromptInputLayout.axaml (matched by WorkspacePickerItem's
        // DataType), so no template wiring is needed here.
        combo.ItemsSource = _workspaceItems;

        var currentIndex = _workspaceItems.IndexOfCwd(_session.Cwd);
        if (currentIndex >= 0)
            combo.SelectedIndex = currentIndex;

        combo.SelectionChanged += (_, _) =>
        {
            var idx = combo.SelectedIndex;
            if (idx < 0 || idx >= _workspaceItems.Count) return;
            var item = _workspaceItems[idx];
            if (item.IsSentinel)
            {
                ChooseFolder();
                return;
            }
            if (!string.IsNullOrEmpty(item.Cwd))
                SwitchWorkspace(item.Cwd);
        };

        // A fresh (unpersisted) session lets the user choose which
        // workspace it belongs to before the first message; once the
        // session has messages the picker disappears (the cwd is
        // committed).
        combo.IsVisible = _session.State.Messages.Count == 0;
        _session.StateChanged += state => _dispatchToUi(() =>
        {
            if (state.Messages.Count > 0 && combo.IsVisible)
                combo.IsVisible = false;
        });
    }

    private void RebuildWorkspaceItems(string cwd)
    {
        _workspaceItems = PromptInputPickerBuilder.BuildWorkspacePickerItems(
            WorkspaceSessionStore.ListWorkspaces(),
            cwd);
    }

    /// <summary>
    /// Switches the fresh session to an existing workspace by navigating to
    /// a new session in that directory. No-op when it already matches.
    /// </summary>
    private void SwitchWorkspace(string cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return;
        if (Path.GetFullPath(cwd) == Path.GetFullPath(_session.Cwd)) return;
        // Defer the navigation out of the picker's SelectionChanged dispatch.
        // Navigating synchronously rebuilds the whole chat page while the
        // ComboBox event is still settling.
        _postToUi(() => _ = NavigateToNewAsync(cwd));
    }

    private async Task NavigateToNewAsync(string? cwd)
    {
        try
        {
            var next = await _session.NewSessionAsync(cwd);
            _active.Replace(next);
        }
        catch (Exception ex)
        {
            DeskLog.Write($"NavigateToNewAsync({cwd}): threw: {ex}");
        }
    }

    private async void ChooseFolder()
    {
        // Re-entrancy guard: the native dialog's open/close can deliver a
        // stray second activation; only ever show it once per click.
        if (_pickingFolder) return;
        _pickingFolder = true;
        try
        {
            // _pickFolder resolves to the platform folder picker (a native
            // NSOpenPanel on macOS). It must run on the UI thread — never
            // Task.Run it, or AppKit throws
            // "NSWindow should only be instantiated on the main thread!"
            // The picker itself is async, so awaiting it here leaves the
            // input responsive without leaving the UI thread.
            var picked = await _pickFolder();
            if (string.IsNullOrEmpty(picked)) return;
            _postToUi(() => _ = NavigateToNewAsync(picked));
        }
        catch (Exception ex)
        {
            DeskLog.Write($"ChooseFolder: threw: {ex}");
        }
        finally
        {
            _pickingFolder = false;
        }
    }

    private void SubmitCurrent()
    {
        var text = (_layout.Editor.Text ?? string.Empty).Trim();
        DeskLog.Write($"SubmitCurrent: text='{text}' len={text.Length} cwd='{_session.Cwd}' running={_session.State.IsRunning}");
        _layout.Editor.Text = string.Empty;
        if (text.Length == 0) return;
        HandleInput(text);
    }

    private void HandleInput(string text)
    {
        DeskLog.Write($"HandleInput: text='{text}' running={_session.State.IsRunning}");

        // Wrap the user's text in the shared SlashInputDispatcher so the
        // Avalonia shell matches the TUI's behaviour for slash commands,
        // skills, extensions, ordinary submits, and steering. The
        // Avalonia-specific side-effects (ActiveSession.Replace,
        // ApplicationLifetime.Shutdown, projector-backed feedback) live in
        // AvaloniaSlashActionSink — the dispatcher itself stays
        // UI-agnostic.
        var sink = new AvaloniaSlashActionSink(
            _session, _active, _providers, _postToUi, _projector);

        var outcome = SlashInputDispatcher.Dispatch(
            text,
            _commands,
            _session.State.IsRunning,
            _extensionDispatcher,
            sink);

        switch (outcome.Kind)
        {
            case SlashDispatchKind.None:
            case SlashDispatchKind.SubmitPrompt:
                return;
            case SlashDispatchKind.Transient:
                if (outcome.Message is { } msg)
                {
                    DeskLog.Write($"HandleInput: transient '{msg}'");
                    // No transient slot in Sprint 5 — surface as a
                    // persistent transcript line so the user sees it.
                    // Sprint 6 will introduce a real transient slot in
                    // the chat page footer.
                    _projector.SubmitPersistentError(msg);
                }
                return;
            case SlashDispatchKind.LoadSkill:
                _ = LoadSkillAsync(outcome.SkillName!, outcome.SkillPrompt);
                return;
        }
    }

    /// <summary>
    /// Loads a skill and submits it as the prompt; the loaded content is
    /// reflected as a user bubble so the submission is visible before the
    /// model's response streams in. Mirrors the TUI's
    /// <c>LoadSkillAsync</c> path.
    /// </summary>
    private async Task LoadSkillAsync(string name, string? prompt)
    {
        try
        {
            var content = await _session.LoadSkillAsync(name, prompt);
            _projector.SubmitUserLine(content);
            // Reset rendered count so the new bubble is picked up by the
            // transcript view's diff against the prior snapshot (TUI uses
            // the same defensive reset).
            _projector.ResetRenderedCount();
        }
        catch (Exception ex)
        {
            DeskLog.Write($"LoadSkillAsync({name}): {ex.Message}");
            _projector.SubmitPersistentError(ex.Message);
        }
    }

    /// <summary>
    /// Syncs the submit button's glyph and tooltip to the session's running
    /// state: a send arrow while idle, a stop glyph (cancels) while a turn
    /// is in flight.
    /// </summary>
    private void UpdateSubmitButton(SessionState state)
    {
        var button = _layout.SubmitButton;
        var icon = _layout.SubmitIcon;
        if (state.IsRunning)
        {
            icon.Kind = MaterialIconKind.Stop;
            button.Background = AvaloniaTheme.Danger;
            ToolTip.SetTip(button, "Stop (Esc)");
        }
        else
        {
            icon.Kind = MaterialIconKind.ArrowUpward;
            button.Background = AvaloniaTheme.Accent;
            ToolTip.SetTip(button, "Submit (Enter)");
        }
    }

    // ──────── Slash auto-complete ────────

    /// <summary>
    /// Hides/shows the auto-complete popup based on the editor's text. A
    /// <c>/</c> token at the caret yields the merged command list; the
    /// skill provider's tokens open the same surface for skills once
    /// <c>/skill:</c> is recognised. Up/Down move the highlight; Tab
    /// accepts the highlighted suggestion into the editor (replacing the
    /// <c>/</c> token); Escape dismisses.
    /// </summary>
    private void ConfigureAutoComplete()
    {
        var list = _layout.AutoCompleteItems;
        list.DoubleTapped += (_, _) => AcceptAutoComplete();
        // The list is hidden initially; the controller flips IsVisible
        // when there's something to show.
        _layout.AutoCompleteRoot.IsVisible = false;

        _layout.Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name is nameof(TextBox.Text) or nameof(TextBox.CaretIndex))
                UpdateAutoComplete(_layout.Editor.Text ?? string.Empty,
                    _layout.Editor.CaretIndex);
        };
        _layout.Editor.KeyDown += (_, e) =>
        {
            if (!_layout.AutoCompleteRoot.IsVisible) return;
            switch (e.Key)
            {
                case Key.Up:
                    MoveAutoComplete(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    MoveAutoComplete(+1);
                    e.Handled = true;
                    break;
                case Key.Tab:
                    AcceptAutoComplete();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    _layout.AutoCompleteRoot.IsVisible = false;
                    e.Handled = true;
                    break;
            }
        };

        _session.StateChanged += _ => UpdateAutoComplete(
            _layout.Editor.Text ?? string.Empty, _layout.Editor.CaretIndex);
    }

    private void UpdateAutoComplete(string text, int caret)
    {
        var provider = new SlashCommandProvider(_commands);
        var match = provider.GetSuggestion(text.AsSpan(), caret);
        if (match is null)
        {
            _layout.AutoCompleteRoot.IsVisible = false;
            return;
        }
        var items = match.Items.Select(i => new SlashAutoCompleteItem(
            i.Replacement, i.Description)).ToList();
        if (items.Count == 0)
        {
            _layout.AutoCompleteRoot.IsVisible = false;
            return;
        }
        _layout.AutoCompleteItems.ItemsSource = items;
        _layout.AutoCompleteItems.SelectedIndex = 0;
        _layout.AutoCompleteRoot.IsVisible = true;
    }

    private void MoveAutoComplete(int delta)
    {
        var list = _layout.AutoCompleteItems;
        if (list.ItemCount == 0) return;
        var next = list.SelectedIndex + delta;
        if (next < 0) next = list.ItemCount - 1;
        if (next >= list.ItemCount) next = 0;
        list.SelectedIndex = next;
    }

    private void AcceptAutoComplete()
    {
        var list = _layout.AutoCompleteItems;
        if (list.SelectedItem is not SlashAutoCompleteItem item) return;
        var editor = _layout.Editor;
        // The provider always exposes items whose Replacement is the
        // full command name (e.g. "/new"), so accepting replaces the
        // current slash-token with the full command and re-queries the
        // autocomplete so a follow-up prompt ("/skill:review") can be
        // typed without the popup re-opening on the next keystroke.
        var current = editor.Text ?? string.Empty;
        var caret = editor.CaretIndex;
        var slashStart = caret;
        while (slashStart > 0 && current[slashStart - 1] != '/')
        {
            slashStart--;
        }
        var before = current[..slashStart];
        var after = current[caret..];
        editor.Text = before + item.Replacement + after;
        editor.CaretIndex = (before + item.Replacement).Length;
        _layout.AutoCompleteRoot.IsVisible = false;
    }

    /// <summary>Exposed auto-complete items (tests).</summary>
    internal IReadOnlyList<SlashAutoCompleteItem> AutoCompleteItems =>
        _layout.AutoCompleteItems.ItemsSource as IReadOnlyList<SlashAutoCompleteItem>
            ?? [];

    /// <summary>True when the auto-complete popup is visible (tests).</summary>
    internal bool AutoCompleteVisible => _layout.AutoCompleteRoot.IsVisible;

    // ──────── Dispatcher helpers ────────

    private static void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
