using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons.Avalonia;
using Phi.Avalonia.Components;
using Phi.Avalonia.Controls;
using Phi.Chat;
using Phi.Extensions;
using Phi.Extensions.Host;
using Phi.Providers;

namespace Phi.Avalonia;

/// <summary>
/// Controller for the desktop shell. Owns navigation state by listening
/// to <see cref="ActiveSession.Changed"/> and to the watched session's
/// title/persistence transitions, and drives view switching in
/// <see cref="ShellLayout"/>'s view host. The chrome itself (sidebar +
/// buttons + dividers) lives in <see cref="ShellLayout"/> as XAML; this
/// class is intentionally free of UI construction beyond the imperative
/// row templates (which wire rename / delete events).
/// </summary>
public sealed class ShellView : IDisposable
{
    private readonly ActiveSession _active;
    private readonly ProviderManager _providers;
    private readonly Action<Action> _dispatchToUi;
    private readonly Action<Action> _postToUi;
    private readonly Action<IUiSink>? _onSinkBuilt;
    private readonly Func<IExtensionRenderers?>? _renderersAccessor;
    private readonly Func<ISlashCommandRegistry?>? _commandRegistryAccessor;
    private readonly Func<IPhiContext?>? _contextAccessor;
    private readonly ShellLayout _layout;

    private ChatPageView? _chatPage;
    private bool _showingChat;
    private bool _rebuilding;
    private ISession? _watchedSession;
    private bool _wasPersisted;
    private string? _lastTitle;
    private NavModel.GroupMode _groupMode = NavModel.GroupMode.ByWorkspace;
    private List<NavModel.Entry> _entries = [];

    // The most recently built UI sink — kept here so actions triggered from
    // the sidebar (e.g. "Reload extensions" on a session row) can surface
    // results / errors through the live UI without re-resolving the bridge.
    private AvaloniaUiSink? _lastSink;

    // The row currently in rename-edit mode, if any, plus the top level
    // whose pointer presses commit the edit (see AttachRenameDismiss).
    private NavModel.Entry? _renameEntry;
    private TextBlock? _renameTitle;
    private TextBox? _renameBox;
    private EllipsisMenu? _renameMenu;
    private TopLevel? _renameTopLevel;

    public ShellView(
        ActiveSession active,
        ProviderManager providers,
        Action<Action>? dispatchToUi = null,
        Action<Action>? postToUi = null,
        Action<IUiSink>? onSinkBuilt = null,
        Func<IExtensionRenderers?>? renderersAccessor = null,
        Func<ISlashCommandRegistry?>? commandRegistryAccessor = null,
        Func<IPhiContext?>? contextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(providers);
        _active = active;
        _providers = providers;
        _dispatchToUi = dispatchToUi ?? Dispatch;
        _postToUi = postToUi ?? Post;
        _onSinkBuilt = onSinkBuilt;
        _renderersAccessor = renderersAccessor;
        _commandRegistryAccessor = commandRegistryAccessor;
        _contextAccessor = contextAccessor;

        _layout = new ShellLayout();

        // Hover background for the flat sidebar nav buttons (New Chat /
        // Providers). The visual state on hover is dynamic — it depends on
        // pointer state — so it stays in code rather than in the XAML's
        // static properties. Model selection lives in PromptInputView's
        // toolbar, so no separate Models sidebar button.
        StyleFlatNavButtonHover(_layout.NewChatButton);
        StyleFlatNavButtonHover(_layout.ProvidersButton);

        _layout.NewChatButton.Click += (_, _) => _postToUi(() =>
        {
            ShowChat();
            _ = NavigateToNewAsync();
        });
        _layout.ByDateButton.Click += (_, _) => SetGroupMode(NavModel.GroupMode.ByDate);
        _layout.ByWorkspaceButton.Click += (_, _) => SetGroupMode(NavModel.GroupMode.ByWorkspace);
        _layout.ProvidersButton.Click += (_, _) => _postToUi(ShowProviders);
        _layout.SessionsList.SelectionChanged += (_, _) => OnSessionSelection();

        // The row template dispatches on entry.Kind (Workspace vs Session)
        // and each branch subscribes to events on its child controls (rename
        // KeyDown/LostFocus, ⋯ menu clicks). Keeping it in code lets the
        // row builders stay imperative; promoting it to XAML would require
        // splitting Entry into a DU so each kind gets its own DataTemplate.
        _layout.SessionsList.ItemTemplate = new FuncDataTemplate<NavModel.Entry>((entry, _) =>
        {
            // ItemsSource swaps on a live ListBox recycle containers and
            // may briefly invoke the template with null data; skip it.
            if (entry is null) return null!;
            return entry.Kind == NavModel.Kind.Workspace
                ? BuildWorkspaceRow(entry)
                : BuildSessionRow(entry);
        });

        _active.Changed += OnActiveSessionChanged;

        RebuildNavigation();
        ShowChat();
    }

    /// <summary>The two-pane root (the <see cref="ShellLayout"/> itself).</summary>
    public Control Root => _layout;

    /// <summary>The right-side view host (used by ShowChat / ShowProviders).</summary>
    public ContentControl ViewHost => _layout.ViewHost;

    /// <summary>The live chat page, when the chat view is active (tests).</summary>
    internal ChatPageView? ChatPage => _chatPage;

    /// <summary>The sessions list (tests).</summary>
    internal ListBox SessionsList => _layout.SessionsList;

    /// <summary>The "By date" toggle button (tests).</summary>
    internal Button ByDateButton => _layout.ByDateButton;

    /// <summary>The "By workspace" toggle button (tests).</summary>
    internal Button ByWorkspaceButton => _layout.ByWorkspaceButton;

    /// <summary>The "New Chat" button (tests).</summary>
    internal Button NewChatButton => _layout.NewChatButton;

    /// <summary>The "Providers" button (tests).</summary>
    internal Button ProvidersButton => _layout.ProvidersButton;

    /// <summary>The active group-mode button (tests).</summary>
    internal bool ByDateActive => _groupMode == NavModel.GroupMode.ByDate;

    /// <summary>The current group mode; setting rebuilds the nav (tests).</summary>
    internal NavModel.GroupMode GroupMode
    {
        get => _groupMode;
        set => SetGroupMode(value);
    }

    private void SetGroupMode(NavModel.GroupMode mode)
    {
        if (_groupMode == mode) return;
        _groupMode = mode;
        UpdateGroupToggle();
        RebuildNavigation();
    }

    /// <summary>
    /// Applies the group-mode toggle's selected state: the active button is
    /// filled with the accent color and its icon turns white (unmistakable),
    /// the inactive one stays transparent with a secondary icon. Done
    /// imperatively (rather than via pseudo-classes) so the visual maps
    /// directly to the asserted brushes in the shell view tests.
    /// </summary>
    private void UpdateGroupToggle()
    {
        ApplyToggle(_layout.ByDateButton, _groupMode == NavModel.GroupMode.ByDate);
        ApplyToggle(_layout.ByWorkspaceButton, _groupMode == NavModel.GroupMode.ByWorkspace);
    }

    private static void ApplyToggle(Button button, bool active)
    {
        if (button.Content is not MaterialIcon icon) return;
        button.Background = active ? AvaloniaTheme.Accent : Brushes.Transparent;
        button.BorderBrush = active ? AvaloniaTheme.Accent : AvaloniaTheme.ControlBorder;
        button.BorderThickness = new Thickness(active ? 0 : 1);
        icon.Foreground = active ? AvaloniaTheme.AccentText : AvaloniaTheme.TextSecondary;
    }

    /// <summary>
    /// Adds the hover background behaviour to a flat sidebar nav button.
    /// The base chrome (transparent background, zero border, padding,
    /// left-aligned content) is declared in <see cref="ShellLayout"/>;
    /// only the dynamic hover fill lives here so the row reads as part
    /// of the sidebar list.
    /// </summary>
    private static void StyleFlatNavButtonHover(Button button)
    {
        button.PointerEntered += (_, _) => button.Background = AvaloniaTheme.ContainerBackground;
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
    }

    // ──────── Session / workspace rows ────────

    /// <summary>
    /// One session row: the session title plus a "⋯" menu with Rename
    /// (the whole row becomes an editable field) and Delete. Selecting the
    /// row resumes the session; the ellipsis button swallows its own
    /// pointer events so it doesn't leak into row selection.
    /// </summary>
    internal Grid BuildSessionRow(NavModel.Entry entry)
    {
        var title = new TextBlock
        {
            Text = entry.Title,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
        };
        // The edit field replaces the whole row while renaming: it fills the
        // row width, styled flush so it reads as the row itself being
        // edited, not an input box sitting inside the row.
        var renameBox = new TextBox
        {
            Text = entry.Title,
            FontSize = 13,
            MinHeight = 0,
            Padding = new Thickness(4, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsVisible = false,
        };

        var menu = new EllipsisMenu();
        menu.AddItem("Rename", () => BeginRename(entry, title, renameBox, menu))
            .AddItem("Reload extensions", () => ReloadExtensions())
            .AddItem("Delete", () => DeleteSessionRow(entry));

        // Enter commits; Esc cancels; blur always ends the edit (committing
        // any change) so the row never stays stuck in edit mode.
        renameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                EndRename(entry, title, renameBox, menu, commit: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                EndRename(entry, title, renameBox, menu, commit: false);
                e.Handled = true;
            }
        };
        renameBox.LostFocus += (_, _) =>
            EndRename(entry, title, renameBox, menu, commit: true);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 2),
        };
        // Title and the rename field share column 0 (only one is visible at a
        // time); the ⋯ menu sits in the fixed Auto column 1. A DockPanel's
        // Left-docked title sized to its desired width and would overlap the
        // menu on long titles — the star column keeps the title constrained
        // so TextTrimming ellipsizes instead.
        Grid.SetColumn(title, 0);
        Grid.SetColumn(renameBox, 0);
        Grid.SetColumn(menu, 1);
        row.Children.Add(title);
        row.Children.Add(renameBox);
        row.Children.Add(menu);
        return row;
    }

    /// <summary>
    /// One workspace row: the workspace label plus a "⋯" menu with New
    /// session and Delete (deletes every session in the workspace). The
    /// label isn't selectable (OnSessionSelection clears any highlight), but
    /// the container stays enabled so the menu button remains clickable.
    /// </summary>
    internal Grid BuildWorkspaceRow(NavModel.Entry entry)
    {
        var label = new TextBlock
        {
            // entry.Title is the display leaf (WorkspaceLeafLabel); the full
            // cwd lives in entry.Cwd for the menu actions. Trimming keeps a
            // long folder name from pushing the ⋯ button out of view.
            Text = entry.Title.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = AvaloniaTheme.TextSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 10, 0, 2),
        };
        var menu = new EllipsisMenu()
            .AddItem("New session", () => NewSessionInWorkspace(entry.Cwd))
            .AddItem("Delete", () => DeleteWorkspaceRow(entry.Cwd));

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        // Label fills the star column (long workspace names ellipsize); the
        // ⋯ menu sits in the fixed Auto column so it never overlaps.
        Grid.SetColumn(label, 0);
        Grid.SetColumn(menu, 1);
        row.Children.Add(label);
        row.Children.Add(menu);
        return row;
    }

    /// <summary>
    /// Switches the row into edit mode: the whole row becomes the rename
    /// field (title and menu hidden so the box fills the row width). While
    /// renaming, any pointer press outside the field (including on
    /// non-focusable areas) commits the edit — focus loss alone isn't
    /// enough because clicking empty space doesn't move keyboard focus.
    /// </summary>
    internal void BeginRename(NavModel.Entry entry, TextBlock title, TextBox renameBox, EllipsisMenu menu)
    {
        _renameEntry = entry;
        _renameTitle = title;
        _renameBox = renameBox;
        _renameMenu = menu;
        title.IsVisible = false;
        menu.IsVisible = false;
        renameBox.Text = entry.Title;
        renameBox.IsVisible = true;
        renameBox.Focus();
        renameBox.CaretIndex = renameBox.Text?.Length ?? 0;
        AttachRenameDismiss();
    }

    /// <summary>
    /// Ends edit mode. When <paramref name="commit"/> is true (Enter / blur /
    /// outside click) the new title is persisted if it changed; Esc cancels
    /// without writing. Either way the row returns to its normal display.
    /// </summary>
    internal void EndRename(NavModel.Entry entry, TextBlock title, TextBox renameBox, EllipsisMenu menu, bool commit)
    {
        // Idempotent: hiding the box raises LostFocus, which calls this
        // again — bail once the edit has already ended.
        if (!renameBox.IsVisible) return;
        DetachRenameDismiss();
        var text = (renameBox.Text ?? string.Empty).Trim();
        if (commit
            && text.Length > 0
            && text != entry.Title
            && entry.SessionId is { } id)
        {
            WorkspaceSessionStore.RenameSession(id, text);
        }
        renameBox.IsVisible = false;
        title.IsVisible = true;
        menu.IsVisible = true;
        _renameEntry = null;
        _renameTitle = null;
        _renameBox = null;
        _renameMenu = null;
        _postToUi(RebuildNavigation);
    }

    // ──────── Commit rename on outside press ────────

    private void AttachRenameDismiss()
    {
        if (_renameBox is null) return;
        _renameTopLevel = TopLevel.GetTopLevel(_renameBox);
        if (_renameTopLevel is null) return;
        _renameTopLevel.AddHandler(
            InputElement.PointerPressedEvent,
            OnRenameOutsidePress,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void DetachRenameDismiss()
    {
        if (_renameTopLevel is null) return;
        _renameTopLevel.RemoveHandler(InputElement.PointerPressedEvent, OnRenameOutsidePress);
        _renameTopLevel = null;
    }

    private void OnRenameOutsidePress(object? sender, PointerPressedEventArgs e)
    {
        // Clicking inside the edit field keeps editing; any other press —
        // including on non-focusable empty space — commits the rename.
        if (_renameBox is { } box
            && e.Source is Visual { } source
            && box.IsVisualAncestorOf(source))
            return;
        if (_renameEntry is { } entry
            && _renameTitle is { } title
            && _renameBox is { } renameBox
            && _renameMenu is { } menu)
        {
            EndRename(entry, title, renameBox, menu, commit: true);
        }
    }

    /// <summary>Commits a session rename from id + new title (tests / menus).</summary>
    internal void RenameSessionById(string id, string title)
    {
        WorkspaceSessionStore.RenameSession(id, title);
        _postToUi(RebuildNavigation);
    }

    private static void CancelRename(TextBlock title, TextBox renameBox)
    {
        renameBox.IsVisible = false;
        title.IsVisible = true;
    }

    internal void DeleteSessionRow(NavModel.Entry entry)
    {
        if (entry.SessionId is not { } id) return;
        var wasActive = _active.Current.State.SessionId == id;
        WorkspaceSessionStore.DeleteSession(id);
        if (wasActive)
            _postToUi(() => _ = NavigateToNewAsync());
        else
            _postToUi(RebuildNavigation);
    }

    internal void NewSessionInWorkspace(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return;
        _postToUi(() => _ = NavigateToNewAsync(cwd));
    }

    internal void DeleteWorkspaceRow(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return;
        var wasActive = Path.GetFullPath(_active.Current.Cwd)
            .Equals(Path.GetFullPath(cwd), StringComparison.OrdinalIgnoreCase);
        WorkspaceSessionStore.DeleteWorkspace(cwd);
        if (wasActive)
            _postToUi(() => _ = NavigateToNewAsync());
        else
            _postToUi(RebuildNavigation);
    }

    // ──────── Navigation model ────────

    public void RebuildNavigation()
    {
        _rebuilding = true;
        try
        {
            _entries = NavModel.BuildMainEntries(
                WorkspaceSessionStore.ListAllSessions(7),
                _groupMode);
            _layout.SessionsList.ItemsSource = _entries;
            _layout.SessionsList.SelectedIndex = NavModel.IndexForActive(
                _entries, _active.Current.State.SessionId);

            UpdateGroupToggle();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void OnSessionSelection()
    {
        if (_rebuilding) return;
        if (_layout.SessionsList.SelectedItem is not NavModel.Entry entry) return;

        // Workspace rows are group titles, not clickable sessions. Clear the
        // highlight synchronously so it never visually sticks; the row's ⋯
        // menu stays usable because the container is not disabled.
        if (entry.Kind == NavModel.Kind.Workspace)
        {
            _layout.SessionsList.SelectedIndex = -1;
            return;
        }

        // Defer the session-resume dispatch out of the ListBox's
        // SelectionChanged dispatch. Navigating (→ Active.Changed →
        // RebuildNavigation) mutates the list items; running that
        // synchronously inside the very dispatch that is handling the
        // click re-enters the ListBox and corrupts its state.
        _postToUi(() => HandleSelection(entry));
    }

    private void HandleSelection(NavModel.Entry entry)
    {
        switch (entry.Kind)
        {
            case NavModel.Kind.Workspace:
                // Group title rows are intercepted in OnSessionSelection;
                // be defensive and clear any stray highlight.
                _layout.SessionsList.SelectedIndex = -1;
                break;
            case NavModel.Kind.Session:
                ShowChat();
                if (entry.SessionId is { } id)
                    _ = ResumeAsync(id);
                break;
        }
    }

    private void OnActiveSessionChanged()
    {
        _dispatchToUi(() =>
        {
            if (_showingChat)
                ShowChat();
            RebuildNavigation();
        });
    }

    // ──────── Navigation (delegated to ISession) ────────

    /// <summary>
    /// Reload the active session's extension set: dispose the current
    /// extension runtime (unloading ALCs, clearing hooks + event
    /// dispatch, invalidating captured <c>IPhiApi</c> references) and
    /// re-run the composition root's <c>ExtensionRuntimeFactory</c> so
    /// CodingPack + every other compiled extension re-register
    /// automatically. Wired to the per-session row's "Reload
    /// extensions" ellipsis-menu entry — Avalonia has no slash
    /// dispatcher by design, so this lives next to Rename / Delete on
    /// the session row.
    /// </summary>
    private void ReloadExtensions()
    {
        try
        {
            _active.Current.ReloadExtensions();
            DeskLog.Write("ShellView.ReloadExtensions: ok");
            _lastSink?.Notify("Extensions reloaded.", NotifyLevel.Info);
        }
        catch (Exception ex)
        {
            DeskLog.Write($"ShellView.ReloadExtensions: threw: {ex}");
            _lastSink?.FlashError($"Reload failed: {ex.Message}", persistent: true);
        }
    }

    private async Task NavigateToNewAsync(string? cwd = null)
    {
        try
        {
            var next = await _active.Current.NewSessionAsync(cwd);
            _active.Replace(next);
        }
        catch (Exception ex)
        {
            DeskLog.Write($"ShellView.NavigateToNewAsync({cwd}): threw: {ex}");
        }
    }

    private async Task ResumeAsync(string id)
    {
        try
        {
            var next = await _active.Current.ResumeAsync(id);
            _active.Replace(next);
        }
        catch (Exception ex)
        {
            DeskLog.Write($"ShellView.ResumeAsync({id}): threw: {ex}");
        }
    }

    // ──────── View builders ────────

    private void ShowChat()
    {
        _chatPage?.Dispose();
        _chatPage = new ChatPageView(
            _active,
            _providers,
            _active.Current,
            pickFolder: PickFolderAsync,
            postToUi: _postToUi,
            dispatchToUi: _dispatchToUi,
            renderers: _renderersAccessor?.Invoke(),
            commandRegistryAccessor: _commandRegistryAccessor,
            contextAccessor: _contextAccessor);
        _layout.ViewHost.Content = _chatPage.Root;
        _showingChat = true;
        WatchSession(_active.Current);
        _postToUi(() => _chatPage?.PromptInput.FocusEditor());

        // Sprint 3: build the extension UI sink for this chat page and
        // notify the composition root so the runtime's PhiUiBridge
        // resolves to it. The sink wraps the page's projector (for
        // persistent transcript lines) + the main window (for dialog
        // ownership); transient notifications also flow through the
        // projector in Stage 1 (until the chat page grows a proper
        // transient slot in Sprint 4). Kept here so sidebar actions
        // (Reload extensions on a session row) can surface results.
        if (_chatPage is { } page)
        {
            var sink = new AvaloniaUiSink(
                page.Projector,
                () => TopLevel.GetTopLevel(_layout) as Window);
            _lastSink = sink;
            _onSinkBuilt?.Invoke(sink);
        }
    }

    /// <summary>
    /// Opens the platform folder picker via the ambient <see cref="TopLevel"/>'s
    /// storage provider. Works for both the desktop window lifetime and
    /// single-view lifetimes (Android, browser).
    /// </summary>
    private async Task<string?> PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(_layout);
        if (topLevel?.StorageProvider is not { } provider) return null;
        if (!provider.CanPickFolder) return null;
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose working directory",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Watches the current session so the nav can pick up the session the
    /// moment its first message persists. A fresh "New Chat" session is not
    /// in the record-derived session list until it is written to disk; once
    /// it persists we rebuild the nav so the new session appears and the
    /// highlight moves to it (instead of staying on "New Chat").
    /// </summary>
    private void WatchSession(ISession session)
    {
        if (ReferenceEquals(_watchedSession, session)) return;
        if (_watchedSession is not null)
            _watchedSession.StateChanged -= OnSessionStateForNav;
        _watchedSession = session;
        _wasPersisted = session.State.IsPersisted;
        _lastTitle = session.State.SessionTitle;
        session.StateChanged += OnSessionStateForNav;
    }

    private void OnSessionStateForNav(SessionState state)
    {
        if (!_wasPersisted && state.IsPersisted)
        {
            _wasPersisted = true;
            RebuildNavigation();
            return;
        }
        // The LLM auto-namer fills in the session title after the first
        // message; refresh the nav so the real title replaces the id-prefix
        // placeholder.
        if (!string.Equals(_lastTitle, state.SessionTitle, StringComparison.Ordinal))
        {
            _lastTitle = state.SessionTitle;
            RebuildNavigation();
        }
    }

    private void ShowProviders()
    {
        _showingChat = false;
        _layout.ViewHost.Content = new ProvidersPage(_providers).Root;
    }

    public void Dispose()
    {
        DetachRenameDismiss();
        _active.Changed -= OnActiveSessionChanged;
        if (_watchedSession is not null)
            _watchedSession.StateChanged -= OnSessionStateForNav;
        _chatPage?.Dispose();
        _chatPage = null;
        _lastSink = null;
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
