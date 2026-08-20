using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Material.Icons;
using Phi.Agent;
using Phi.Chat;
using Phi.Prompt;
using Phi.Providers;
using Phi.Sessions;

namespace Phi.Avalonia.Components;

/// <summary>
/// The prompt input controller. Owns the input's behaviour — dispatching
/// editor text to the session (or as steering when a run is in flight),
/// the model picker (every provider's models grouped by provider with the
/// current one marked), the workspace picker (fresh sessions only), and
/// the submit button's idle/running glyph — and wires it onto the named
/// controls of <see cref="PromptInputLayout"/>.
/// <para>
/// Enter submits, Shift+Enter inserts a newline, Esc cancels the running
/// turn. Slash-command completion and dispatch are intentionally not
/// exposed: navigation, connect, models, exit, etc. are reachable via the
/// side bar; the input only knows about plain user messages.
/// </para>
/// </summary>
public sealed class PromptInputView
{
    private readonly ISession _session;
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;
    private readonly ChatTranscriptProjector _projector;
    private readonly Func<Task<string?>> _pickFolder;
    private readonly Action<Action> _postToUi;
    private readonly Action<Action> _dispatchToUi;
    private readonly PromptInputLayout _layout;

    private bool _suppressModelSelection;
    private bool _pickingFolder;

    private IReadOnlyList<ModelPickerItem> _modelItems = Array.Empty<ModelPickerItem>();
    private IReadOnlyList<WorkspacePickerItem> _workspaceItems = Array.Empty<WorkspacePickerItem>();

    public PromptInputView(
        ISession session,
        ISessionNavigator navigator,
        ProviderManager providers,
        ChatTranscriptProjector projector,
        Func<Task<string?>>? pickFolder = null,
        Action<Action>? postToUi = null,
        Action<Action>? dispatchToUi = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(projector);

        _session = session;
        _navigator = navigator;
        _providers = providers;
        _projector = projector;
        _pickFolder = pickFolder ?? (() => Task.FromResult<string?>(null));
        _postToUi = postToUi ?? Post;
        _dispatchToUi = dispatchToUi ?? Dispatch;

        _layout = new PromptInputLayout();

        WireEditor();
        WireSubmitButton();
        ConfigureModelCombo();
        ConfigureWorkspaceCombo();
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
            _providers.Providers,
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
            var provider = _providers.CreateProvider(entry, apiKey);
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
    /// directory via <see cref="ISessionNavigator.NavigateToNewAsync(string?)"/>.
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
        _postToUi(() => _ = _navigator.NavigateToNewAsync(cwd));
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
            _postToUi(() => _ = _navigator.NavigateToNewAsync(picked));
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