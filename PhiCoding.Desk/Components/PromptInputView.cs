using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using PhiAgent;
using PhiCoding.Chat;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Desk.Components;

/// <summary>
/// The prompt input shell. A rounded <see cref="Border"/> containing a
/// <see cref="MultiLineTextBox"/> plus a footer row holding the model
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
    private readonly Func<string?> _pickFolder;
    private readonly Action<Action> _postToUi;

    private readonly ObservableValue<string> _text = new(string.Empty);
    private readonly ObservableValue<bool> _workspacePickerVisible = new(false);

    private MultiLineTextBox? _editor;

    private IReadOnlyList<ModelPickerItem> _modelItems = Array.Empty<ModelPickerItem>();
    private IReadOnlyList<WorkspacePickerItem> _workspaceItems = Array.Empty<WorkspacePickerItem>();

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

    /// <summary>The model picker ComboBox, when built (tests).</summary>
    internal ComboBox? ModelComboBox { get; private set; }

    /// <summary>The built model picker item list (tests).</summary>
    internal IReadOnlyList<ModelPickerItem> ModelItems => _modelItems;

    /// <summary>The built workspace picker item list (tests).</summary>
    internal IReadOnlyList<WorkspacePickerItem> WorkspaceItems => _workspaceItems;

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
    /// Builds the input shell: constructs the editor, footer pickers, and
    /// submit button. Must be called exactly once before <see cref="Root"/>
    /// is accessed.
    /// </summary>
    public void Build()
    {
        _editor = new MultiLineTextBox()
            .BindText(_text)
            .Placeholder("Ask Phi anything…")
            .Wrap(true)
            .FontFamily("Consolas")
            .MinHeight(48)
            .MaxHeight(200);

        var modelCombo = BuildModelCombo();
        ModelComboBox = modelCombo;

        _workspacePickerVisible.Value = _session.State.Messages.Count == 0;
        _session.StateChanged += OnSessionStateForPicker;
        var workspaceCombo = BuildWorkspaceCombo();
        WorkspaceComboBox = workspaceCombo;

        var submitButton = new Button()
            .Content("↑", accessKey: false)
            .OnClick(SubmitCurrent)
            .WithTheme((t, c) => c.Background(t.Palette.Accent).Foreground(t.Palette.AccentText));

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

        var footer = new DockPanel()
            .LastChildFill()
            .Spacing(6)
            .Children(
                modelCombo.DockLeft(),
                workspaceCombo.DockLeft(),
                submitButton.DockRight());

        var stack = new StackPanel()
            .Orientation(Aprillz.MewUI.Orientation.Vertical)
            .Spacing(6)
            .Children(_editor, footer);

        Root = new Border()
            .Padding(8, 6)
            .BorderThickness(1)
            .WithTheme((t, c) => c.BorderBrush(t.Palette.ControlBorder))
            .CornerRadius(8)
            .Child(stack);
    }

    private void OnSessionStateForPicker(SessionState state)
    {
        if (state.Messages.Count > 0 && _workspacePickerVisible.Value)
        {
            _workspacePickerVisible.Value = false;
        }
    }

    // ──────── Model picker ────────

    /// <summary>
    /// Builds the model picker footer: a <see cref="ComboBox"/> whose popup
    /// lists every connected provider's models, grouped by provider with
    /// a styled header row above each group. The current provider + model
    /// are listed first; that row is prefixed with ✓. Selecting a row
    /// constructs the live <see cref="IPhiProvider"/> (via
    /// <see cref="ProviderManager"/>) and calls
    /// <see cref="ISession.SwitchProvider"/>. Selection of a header row is
    /// ignored.
    /// </summary>
    private ComboBox BuildModelCombo()
    {
        RebuildModelItems(_session.State.ProviderName, _session.State.Model);

        var combo = new ComboBox()
            .MinWidth(160)
            .MaxWidth(260)
            .Placeholder("Select model")
            .ItemHeight(22)
            .Items(_modelItems, item => item.Label)
            .ItemTemplate<ModelPickerItem>(
                build: ctx => new Aprillz.MewUI.Controls.TextBlock()
                    .Register(ctx, "Label")
                    .TextWrapping(TextWrapping.NoWrap),
                bind: (view, item, _, ctx) =>
                {
                    var label = ctx.Get<Aprillz.MewUI.Controls.TextBlock>("Label");
                    label.Text = item.Label;
                    label.WithTheme((t, c) =>
                        c.Foreground(item.IsHeader
                            ? DeskTheme.TextSecondary(t)
                            : (item.IsCurrent
                                ? t.Palette.Accent
                                : t.Palette.WindowText))
                        .FontWeight(item.IsHeader
                            ? FontWeight.Bold
                            : (item.IsCurrent ? FontWeight.SemiBold : FontWeight.Normal)));
                });

        var currentIndex = _modelItems.IndexOfFirstSelectable();
        if (currentIndex >= 0)
            combo.SelectedIndex = currentIndex;

        combo.SelectionChanged += _ =>
        {
            var idx = combo.SelectedIndex;
            if (idx < 0 || idx >= _modelItems.Count) return;
            var item = _modelItems[idx];
            if (item.IsHeader || item.Entry is null || item.Model is null) return;
            ApplyModelSelection(item.Entry, item.Model);
        };

        _session.StateChanged += OnSessionStateForModel;
        return combo;
    }

    private void OnSessionStateForModel(SessionState state)
    {
        RebuildModelItems(state.ProviderName, state.Model);
        if (ModelComboBox is null) return;
        var idx = _modelItems.IndexOfFirstSelectable();
        if (idx >= 0 && ModelComboBox.SelectedIndex != idx)
        {
            ModelComboBox.SelectedIndex = idx;
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
    /// folder dialog. The native dialog is only ever triggered from the
    /// sentinel's click — keeping it out of the ComboBox's regular
    /// selection handling avoids the modal dialog's close from re-firing
    /// <c>SelectionChanged</c> and popping the dialog twice. Selecting a
    /// workspace (or picking a folder) recreates the fresh session in
    /// that directory via
    /// <see cref="ISessionNavigator.NavigateToNewAsync(string?)"/>.
    /// </summary>
    private ComboBox BuildWorkspaceCombo()
    {
        RebuildWorkspaceItems(_session.Cwd);

        var combo = new ComboBox()
            .MinWidth(160)
            .MaxWidth(260)
            .Placeholder("Select workspace")
            .ItemHeight(22)
            .Items(_workspaceItems, item => item.Label)
            .ItemTemplate<WorkspacePickerItem>(
                build: ctx => new Aprillz.MewUI.Controls.TextBlock()
                    .Register(ctx, "Label")
                    .TextWrapping(TextWrapping.NoWrap),
                bind: (view, item, _, ctx) =>
                {
                    var label = ctx.Get<Aprillz.MewUI.Controls.TextBlock>("Label");
                    label.Text = item.Label;
                    label.WithTheme((t, c) =>
                        c.Foreground(item.IsSentinel
                            ? DeskTheme.TextSecondary(t)
                            : t.Palette.WindowText)
                        .FontWeight(item.IsSentinel
                            ? FontWeight.SemiBold
                            : FontWeight.Normal));
                });

        var currentIndex = _workspaceItems.IndexOfCwd(_session.Cwd);
        if (currentIndex >= 0)
            combo.SelectedIndex = currentIndex;

        combo.SelectionChanged += _ =>
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

        combo.BindIsVisible(_workspacePickerVisible);
        return combo;
    }

    private void RebuildWorkspaceItems(string cwd)
    {
        _workspaceItems = PromptInputPickerBuilder.BuildWorkspacePickerItems(
            WorkspaceSessionStore.ListWorkspaces(),
            cwd);
        if (WorkspaceComboBox is null) return;
        WorkspaceComboBox.ItemsSource = ItemsView.Create(_workspaceItems, item => item.Label);
        var currentIndex = _workspaceItems.IndexOfCwd(cwd);
        if (currentIndex >= 0)
            WorkspaceComboBox.SelectedIndex = currentIndex;
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
            _postToUi(() => _navigator.NavigateToNewAsync(picked));
        }
        finally
        {
            _pickingFolder = false;
        }
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
}

// ──────── Picker item records (also consumed by tests) ────────

/// <summary>
/// One row in the model picker's dropdown. <see cref="IsHeader"/> rows
/// are styled as provider group dividers and ignored on selection; the
/// remaining rows carry the <see cref="Entry"/> + <see cref="Model"/> pair
/// passed to <see cref="ISession.SwitchProvider"/>.
/// </summary>
public sealed record ModelPickerItem
{
    public required string Label { get; init; }
    public required bool IsHeader { get; init; }
    public ProviderCatalogEntry? Entry { get; init; }
    public string? Model { get; init; }

    /// <summary>True for the currently-active provider/model row.</summary>
    public bool IsCurrent { get; init; }
}

/// <summary>
/// One row in the workspace picker's dropdown. <see cref="IsSentinel"/>
/// is the trailing "📁 Choose folder…" entry that opens the native
/// folder dialog; the remaining rows switch to a fresh session in their
/// <see cref="Cwd"/>.
/// </summary>
public sealed record WorkspacePickerItem
{
    public required string Label { get; init; }
    public required bool IsSentinel { get; init; }
    public required string Cwd { get; init; }
}

/// <summary>
/// Pure builders for the picker item lists. Exposed at namespace level so
/// tests can assert the rendering contract without spinning up a session.
/// </summary>
public static class PromptInputPickerBuilder
{
    /// <summary>
    /// Ordered list backing the model dropdown. The current provider's
    /// models come first, followed by the rest alphabetically. Each
    /// provider group is preceded by a header row carrying its
    /// <see cref="ProviderCatalogEntry.DisplayName"/>.
    /// </summary>
    public static IReadOnlyList<ModelPickerItem> BuildModelPickerItems(
        IEnumerable<ProviderCatalogEntry> providers,
        string currentProviderName,
        string currentModel,
        Func<ProviderCatalogEntry, bool> hasApiKey)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(hasApiKey);

        var ordered = providers
            .OrderBy(p => string.Equals(p.Name, currentProviderName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<ModelPickerItem>();
        foreach (var p in ordered)
        {
            items.Add(new ModelPickerItem
            {
                Label = $"  {p.DisplayName}",
                IsHeader = true,
            });
            foreach (var m in p.Models)
            {
                var isCurrent = string.Equals(p.Name, currentProviderName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m, currentModel, StringComparison.Ordinal);
                items.Add(new ModelPickerItem
                {
                    Label = isCurrent
                        ? $"    ✓ {p.Name} · {m}"
                        : $"    {p.Name} · {m}",
                    IsHeader = false,
                    Entry = p,
                    Model = m,
                    IsCurrent = isCurrent,
                });
            }
        }
        return items;
    }

    /// <summary>
    /// Ordered list backing the workspace dropdown: distinct workspaces
    /// derived from session records, plus the current cwd if missing, plus
    /// a trailing "📁 Choose folder…" sentinel row.
    /// </summary>
    public static IReadOnlyList<WorkspacePickerItem> BuildWorkspacePickerItems(
        IEnumerable<string> knownWorkspaces,
        string cwd)
    {
        ArgumentNullException.ThrowIfNull(knownWorkspaces);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<WorkspacePickerItem>();
        foreach (var w in knownWorkspaces)
        {
            var full = Path.GetFullPath(w);
            if (!seen.Add(full)) continue;
            list.Add(new WorkspacePickerItem
            {
                Label = WorkspaceLabel(full),
                IsSentinel = false,
                Cwd = full,
            });
        }
        if (!string.IsNullOrEmpty(cwd))
        {
            var fullCwd = Path.GetFullPath(cwd);
            if (seen.Add(fullCwd))
                list.Insert(0, new WorkspacePickerItem
                {
                    Label = WorkspaceLabel(fullCwd),
                    IsSentinel = false,
                    Cwd = fullCwd,
                });
        }
        list.Add(new WorkspacePickerItem
        {
            Label = "📁 Choose folder…",
            IsSentinel = true,
            Cwd = string.Empty,
        });
        return list;
    }

    private static string WorkspaceLabel(string fullPath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fullPath.StartsWith(home, StringComparison.Ordinal)
            ? "~" + fullPath[home.Length..]
            : fullPath;
    }
}

internal static class PromptInputPickerExtensions
{
    /// <summary>Index of the first selectable (non-header, non-sentinel) item.</summary>
    public static int IndexOfFirstSelectable(this IReadOnlyList<ModelPickerItem> items)
    {
        for (var i = 0; i < items.Count; i++)
            if (!items[i].IsHeader) return i;
        return -1;
    }

    /// <summary>Index of the row whose <see cref="WorkspacePickerItem.Cwd"/> matches.</summary>
    public static int IndexOfCwd(this IReadOnlyList<WorkspacePickerItem> items, string cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return -1;
        var full = Path.GetFullPath(cwd);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsSentinel) continue;
            if (string.Equals(Path.GetFullPath(items[i].Cwd), full, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}