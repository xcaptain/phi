using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using PhiAgent;
using PhiCoding.Chat;
using PhiCoding.Prompt;
using PhiCoding.Providers;
using PhiCoding.Sessions;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace PhiCoding.Avalonia.Components;

/// <summary>
/// The prompt input shell. A rounded <see cref="Border"/> containing a
/// multi-line <see cref="TextBox"/> plus a footer row holding the model
/// picker, the workspace picker (only on fresh sessions), and the submit
/// button. Enter submits, Shift+Enter inserts a newline, Esc cancels the
/// running turn. Slash-command completion and dispatch are intentionally
/// not exposed: navigation, connect, models, exit, etc. are reachable via
/// the side bar; the input only knows about plain user messages.
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

    private TextBox? _editor;
    private Button? _submitButton;
    private MaterialIcon? _submitIcon;
    private bool _suppressModelSelection;

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
    }

    public Control Root { get; private set; } = null!;

    /// <summary>The session this input is bound to (tests).</summary>
    internal ISession Session => _session;

    /// <summary>The prompt editor (tests).</summary>
    internal TextBox Editor => _editor!;

    /// <summary>Whether the workspace picker is shown (tests).</summary>
    internal bool WorkspacePickerVisible => WorkspaceComboBox?.IsVisible ?? false;

    /// <summary>The workspace picker ComboBox, when built (tests).</summary>
    internal ComboBox? WorkspaceComboBox { get; private set; }

    /// <summary>The model picker ComboBox, when built (tests).</summary>
    internal ComboBox? ModelComboBox { get; private set; }

    /// <summary>The built model picker item list (tests).</summary>
    internal IReadOnlyList<ModelPickerItem> ModelItems => _modelItems;

    /// <summary>The built workspace picker item list (tests).</summary>
    internal IReadOnlyList<WorkspacePickerItem> WorkspaceItems => _workspaceItems;

    /// <summary>Drives the input dispatch directly (tests). Reads the
    /// current editor text.</summary>
    internal void SubmitForTest() => SubmitCurrent();

    /// <summary>The submit button, when built (tests).</summary>
    internal Button? SubmitButton => _submitButton;

    /// <summary>The submit button's current glyph, when built (tests).</summary>
    internal MaterialIconKind? SubmitIconKind => _submitIcon?.Kind;

    /// <summary>Triggers a workspace switch as a picker selection would (tests).</summary>
    internal void SelectWorkspaceForTest(string cwd) => SwitchWorkspace(cwd);

    /// <summary>Moves keyboard focus to the prompt editor.</summary>
    public void FocusEditor() => _editor?.Focus();

    /// <summary>
    /// Builds the input shell: a single rounded <see cref="Border"/> that
    /// hosts the multi-line editor on top and, docked to its bottom, a
    /// toolbar holding the model picker, the workspace picker (fresh
    /// sessions only) and the submit button — the classic chat-input
    /// layout where everything lives inside one box. Enter submits,
    /// Shift+Enter inserts a newline, Esc cancels the running turn.
    /// Slash-command completion and dispatch are intentionally not
    /// exposed: navigation, connect, models, exit, etc. are reachable via
    /// the side bar; the input only knows about plain user messages.
    /// </summary>
    public Control Build()
    {
        // Transparent editor so it blends into the container's chrome
        // rather than drawing its own box inside the box. The stock Fluent
        // theme redraws a border on focus/pointerover, so swap in a fully
        // borderless template (MakeBorderlessEditor).
        _editor = new TextBox
        {
            PlaceholderText = "Ask Phi anything…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 48,
            MaxHeight = 200,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        MakeBorderlessEditor(_editor);

        var modelCombo = BuildModelCombo();
        ModelComboBox = modelCombo;

        var workspaceCombo = BuildWorkspaceCombo();
        WorkspaceComboBox = workspaceCombo;

        var submitButton = new Button
        {
            Background = AvaloniaTheme.Accent,
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(17),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var submitIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.ArrowUpward,
            Width = 18,
            Height = 18,
            Foreground = AvaloniaTheme.AccentText,
        };
        submitButton.Content = submitIcon;
        _submitButton = submitButton;
        _submitIcon = submitIcon;
        submitButton.Click += (_, _) =>
        {
            // While a turn runs, the button becomes a stop control; once
            // idle it submits. Esc in the editor also cancels.
            if (_session.State.IsRunning)
                _session.Cancel();
            else
                SubmitCurrent();
        };
        ToolTip.SetTip(submitButton, "Submit (Enter)");
        UpdateSubmitButton(_session.State);
        _session.StateChanged += state => _dispatchToUi(() => UpdateSubmitButton(state));

        _editor.KeyDown += (_, e) =>
        {
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

        DockPanel.SetDock(modelCombo, Dock.Left);
        DockPanel.SetDock(workspaceCombo, Dock.Left);
        DockPanel.SetDock(submitButton, Dock.Right);

        // Bottom toolbar: pickers on the left, submit on the right,
        // everything inside the input box's rounded chrome.
        var footer = new DockPanel
        {
            LastChildFill = true,
            HorizontalSpacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { modelCombo, workspaceCombo, submitButton },
        };

        var stack = new StackPanel
        {
            Spacing = 0,
            Children = { _editor, footer },
        };

        Root = new Border
        {
            Padding = new Thickness(12, 8, 10, 8),
            BorderThickness = new Thickness(1),
            BorderBrush = AvaloniaTheme.ControlBorder,
            CornerRadius = new CornerRadius(12),
            Child = stack,
        };
        return Root;
    }

    // ──────── Model picker ────────

    /// <summary>
    /// Builds the model picker footer: a <see cref="ComboBox"/> whose popup
    /// lists every provider's models, grouped by provider with a styled
    /// header row above each group. The current provider + model are
    /// listed first and marked with ✓. Selecting a row constructs the live
    /// <see cref="IPhiProvider"/> and calls
    /// <see cref="ISession.SwitchProvider"/>; header rows are ignored.
    /// </summary>
    private ComboBox BuildModelCombo()
    {
        RebuildModelItems(_session.State.ProviderName, _session.State.Model);

        var combo = new ComboBox
        {
            MinWidth = 180,
            MaxWidth = 320,
            PlaceholderText = "Select model",
            ItemsSource = _modelItems,
            ItemTemplate = new FuncDataTemplate<ModelPickerItem>((item, _) =>
            {
                var label = new TextBlock { Text = item.Label };
                if (item.IsHeader)
                {
                    label.Foreground = AvaloniaTheme.TextSecondary;
                    label.FontWeight = FontWeight.Bold;
                }
                else if (item.IsCurrent)
                {
                    label.Foreground = AvaloniaTheme.Accent;
                    label.FontWeight = FontWeight.SemiBold;
                }
                return label;
            }),
        };
        StyleAsToolbarPicker(combo);

        var currentIndex = _modelItems.IndexOfFirstSelectable();
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
        return combo;
    }

    private void OnSessionStateForModel(SessionState state)
    {
        RebuildModelItems(state.ProviderName, state.Model);
        if (ModelComboBox is null) return;
        var idx = _modelItems.IndexOfFirstSelectable();
        if (idx >= 0 && ModelComboBox.SelectedIndex != idx)
        {
            _suppressModelSelection = true;
            try
            {
                ModelComboBox.ItemsSource = null;
                ModelComboBox.ItemsSource = _modelItems;
                ModelComboBox.SelectedIndex = idx;
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
    /// Builds the workspace picker footer: a single <see cref="ComboBox"/>
    /// listing the distinct workspaces derived from session records plus
    /// the session's current cwd if it isn't already present, with a
    /// trailing "📁 Choose folder…" sentinel row that opens the native
    /// folder dialog. Selecting a workspace (or picking a folder)
    /// recreates the fresh session in that directory via
    /// <see cref="ISessionNavigator.NavigateToNewAsync(string?)"/>.
    /// </summary>
    private ComboBox BuildWorkspaceCombo()
    {
        RebuildWorkspaceItems(_session.Cwd);

        var combo = new ComboBox
        {
            MinWidth = 180,
            MaxWidth = 320,
            PlaceholderText = "Select workspace",
            ItemsSource = _workspaceItems,
            ItemTemplate = new FuncDataTemplate<WorkspacePickerItem>((item, _) =>
            {
                var label = new TextBlock { Text = item.Label };
                if (item.IsSentinel)
                {
                    label.Foreground = AvaloniaTheme.TextSecondary;
                    label.FontWeight = FontWeight.SemiBold;
                }
                return label;
            }),
        };
        StyleAsToolbarPicker(combo);

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
        return combo;
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

    private bool _pickingFolder;

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
        var text = (_editor?.Text ?? string.Empty).Trim();
        DeskLog.Write($"SubmitCurrent: text='{text}' len={text.Length} cwd='{_session.Cwd}' running={_session.State.IsRunning}");
        if (_editor is not null) _editor.Text = string.Empty;
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
        if (_submitButton is null || _submitIcon is null) return;
        if (state.IsRunning)
        {
            _submitIcon.Kind = MaterialIconKind.Stop;
            _submitButton.Background = AvaloniaTheme.Danger;
            ToolTip.SetTip(_submitButton, "Stop (Esc)");
        }
        else
        {
            _submitIcon.Kind = MaterialIconKind.ArrowUpward;
            _submitButton.Background = AvaloniaTheme.Accent;
            ToolTip.SetTip(_submitButton, "Submit (Enter)");
        }
    }

    /// <summary>
    /// Flattens a <see cref="ComboBox"/> so it reads as part of the input
    /// box's toolbar instead of a standalone control: no background, no
    /// border, just the text + drop-down arrow.
    /// </summary>
    private static void StyleAsToolbarPicker(ComboBox combo)
    {
        combo.Background = Brushes.Transparent;
        combo.BorderThickness = new Thickness(0);
        combo.CornerRadius = new CornerRadius(6);
    }

    /// <summary>
    /// Replaces the editor's Fluent <c>ControlTheme</c> with a minimal
    /// borderless template. The stock theme draws a border on the template's
    /// <c>PART_BorderElement</c> via <c>:focus</c>/<c>:pointerover</c>
    /// visual-state styles that ignore the control's own
    /// <c>BorderThickness</c>, so the editor keeps showing a box inside the
    /// input container no matter what we set on the control. This template
    /// renders only the placeholder + <c>TextPresenter</c> inside a
    /// <c>ScrollViewer</c> — no border element at all — so the editor blends
    /// into the rounded input chrome and the footer toolbar reads as part of
    /// the same box.
    /// </summary>
    private static void MakeBorderlessEditor(TextBox editor)
    {
        editor.Template = new FuncControlTemplate((templated, scope) =>
        {
            var textBox = (TextBox)templated;

            var placeholder = new TextBlock
            {
                Foreground = textBox.PlaceholderForeground,
                Text = textBox.PlaceholderText,
                TextWrapping = textBox.TextWrapping,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
            };
            // Show the placeholder only while the box is empty.
            placeholder.Bind(
                Visual.IsVisibleProperty,
                textBox.GetBindingObservable(TextBox.TextProperty, (string? s) => string.IsNullOrEmpty(s)));

            var presenter = new TextPresenter
            {
                Name = "PART_TextPresenter",
                TextWrapping = textBox.TextWrapping,
                SelectionBrush = textBox.SelectionBrush,
                CaretBrush = textBox.CaretBrush,
                VerticalAlignment = VerticalAlignment.Top,
            };
            presenter.RegisterInNameScope(scope);
            // The editor keeps the source-of-truth Text (TwoWay so typed text
            // flows back); TextBox pushes caret/selection during editing, but
            // the initial programmatic value needs the binding.
            presenter.Bind(
                TextPresenter.TextProperty,
                new TemplateBinding(TextBox.TextProperty) { Mode = BindingMode.TwoWay });
            presenter.Bind(TextPresenter.CaretIndexProperty, new TemplateBinding(TextBox.CaretIndexProperty));
            presenter.Bind(TextPresenter.SelectionStartProperty, new TemplateBinding(TextBox.SelectionStartProperty));
            presenter.Bind(TextPresenter.SelectionEndProperty, new TemplateBinding(TextBox.SelectionEndProperty));
            presenter.Bind(TextPresenter.SelectionBrushProperty, new TemplateBinding(TextBox.SelectionBrushProperty));
            presenter.Bind(TextPresenter.SelectionForegroundBrushProperty, new TemplateBinding(TextBox.SelectionForegroundBrushProperty));
            presenter.Bind(TextPresenter.CaretBrushProperty, new TemplateBinding(TextBox.CaretBrushProperty));

            var scrollViewer = new ScrollViewer
            {
                Name = "PART_ScrollViewer",
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent,
                Content = new Panel
                {
                    Children = { placeholder, presenter },
                },
            };
            scrollViewer.RegisterInNameScope(scope);

            return scrollViewer;
        });
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
