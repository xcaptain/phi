using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using PhiCoding.Avalonia.Components;
using PhiCoding.Avalonia.Controls;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Avalonia;

/// <summary>
/// The two-pane shell: a left column (New Chat, a sessions list with a
/// "By date / By workspace" grouping toggle, footer Models/Providers
/// buttons) and a single <see cref="ContentControl"/> host on the right
/// that displays whichever view is active. Selecting a session resumes
/// it; the chat page is rebuilt when
/// <see cref="ISessionNavigator.SessionChanged"/> fires.
/// </summary>
public sealed class ShellView : IDisposable
{
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;
    private readonly Action<Action> _dispatchToUi;
    private readonly Action<Action> _postToUi;

    private ChatPageView? _chatPage;
    private bool _showingChat;
    private bool _rebuilding;
    private ISession? _watchedSession;
    private bool _wasPersisted;
    private string? _lastTitle;
    private NavModel.GroupMode _groupMode = NavModel.GroupMode.ByWorkspace;
    private List<NavModel.Entry> _entries = [];

    private readonly ListBox _sessionsList;
    private readonly Button _byDateButton;
    private readonly Button _byWorkspaceButton;
    private readonly Button _newChatButton;
    private readonly Button _modelsButton;
    private readonly Button _providersButton;

    public ShellView(
        ISessionNavigator navigator,
        ProviderManager providers,
        Action<Action>? dispatchToUi = null,
        Action<Action>? postToUi = null)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        _navigator = navigator;
        _providers = providers;
        _dispatchToUi = dispatchToUi ?? Dispatch;
        _postToUi = postToUi ?? Post;

        var newChatButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new MaterialIcon { Kind = MaterialIconKind.Plus, Width = 16, Height = 16 },
                    new TextBlock { Text = "New Chat", VerticalAlignment = VerticalAlignment.Center },
                },
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(8, 8, 8, 4),
        };
        StyleAsFlatNavButton(newChatButton);
        _newChatButton = newChatButton;
        newChatButton.Click += (_, _) => _postToUi(() =>
        {
            ShowChat();
            _ = _navigator.NavigateToNewAsync();
        });

        // Icon-only group-mode toggle. The active mode gets a filled accent
        // background so the selection is unmistakable (see UpdateGroupToggle);
        // tooltips keep the meaning discoverable without taking row width.
        _byDateButton = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.CalendarMonth, Width = 14, Height = 14 },
            FontSize = 12,
            Padding = new Thickness(6, 2),
            CornerRadius = new CornerRadius(4),
        };
        _byWorkspaceButton = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.Folder, Width = 14, Height = 14 },
            FontSize = 12,
            Padding = new Thickness(6, 2),
            CornerRadius = new CornerRadius(4),
        };
        ToolTip.SetTip(_byDateButton, "By date");
        ToolTip.SetTip(_byWorkspaceButton, "By workspace");
        _byDateButton.Click += (_, _) => SetGroupMode(NavModel.GroupMode.ByDate);
        _byWorkspaceButton.Click += (_, _) => SetGroupMode(NavModel.GroupMode.ByWorkspace);

        // Sessions header row: a two-column grid — "会话" on the left, the
        // group-mode toggle buttons pinned to the right edge.
        var sessionsHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(12, 6, 8, 2),
        };
        var sessionsLabel = new TextBlock
        {
            Text = "会话",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = AvaloniaTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(sessionsLabel, 0);
        var toggleButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _byDateButton, _byWorkspaceButton },
        };
        Grid.SetColumn(toggleButtons, 1);
        sessionsHeader.Children.Add(sessionsLabel);
        sessionsHeader.Children.Add(toggleButtons);

        _sessionsList = new ListBox
        {
            Margin = new Thickness(8, 4),
            ItemTemplate = new FuncDataTemplate<NavModel.Entry>((entry, _) =>
            {
                // ItemsSource swaps on a live ListBox recycle containers and
                // may briefly invoke the template with null data; skip it.
                if (entry is null) return null!;
                return entry.Kind == NavModel.Kind.Workspace
                    ? BuildWorkspaceRow(entry)
                    : BuildSessionRow(entry);
            }),
        };
        // Workspace rows are group titles: they must stay selectable-free
        // WITHOUT disabling the container (a disabled container also kills
        // the row's ⋯ menu button). Selection of a workspace row is instead
        // neutralized in HandleSelection by clearing the highlight.
        _sessionsList.SelectionChanged += (_, _) => OnSessionSelection();

        var modelsButton = new Button
        {
            Content = BuildToggleContent(MaterialIconKind.CubeOutline, "Models"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        StyleAsFlatNavButton(modelsButton);
        _modelsButton = modelsButton;
        modelsButton.Click += (_, _) => _postToUi(ShowModels);
        var providersButton = new Button
        {
            Content = BuildToggleContent(MaterialIconKind.TuneVariant, "Providers"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        StyleAsFlatNavButton(providersButton);
        _providersButton = providersButton;
        providersButton.Click += (_, _) => _postToUi(ShowProviders);

        var footer = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(8, 8, 8, 8),
            Children = { modelsButton, providersButton },
        };

        // Separators: one under the New Chat header, one above the footer.
        // A shared factory keeps the divider height/brush consistent.
        Border Divider() => new()
        {
            Height = 1,
            Background = AvaloniaTheme.ControlBorder,
            Margin = new Thickness(8, 0),
        };
        var topDivider = Divider();
        var bottomDivider = Divider();

        // Three-section left pane: header (New Chat) / sessions /
        // footer. Auto rows for chrome and a star row for the session
        // list, so the ListBox fills the remaining height and scrolls
        // instead of overflowing.
        var leftPane = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
        };
        Grid.SetRow(newChatButton, 0);
        Grid.SetRow(topDivider, 1);
        Grid.SetRow(sessionsHeader, 2);
        Grid.SetRow(_sessionsList, 3);
        Grid.SetRow(bottomDivider, 4);
        Grid.SetRow(footer, 5);
        leftPane.Children.Add(newChatButton);
        leftPane.Children.Add(topDivider);
        leftPane.Children.Add(sessionsHeader);
        leftPane.Children.Add(_sessionsList);
        leftPane.Children.Add(bottomDivider);
        leftPane.Children.Add(footer);

        var leftBorder = new Border
        {
            Width = 240,
            BorderBrush = AvaloniaTheme.ControlBorder,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = leftPane,
        };
        Grid.SetColumn(leftBorder, 0);

        ViewHost = new ContentControl();
        Grid.SetColumn(ViewHost, 1);

        Root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { leftBorder, ViewHost },
        };

        _navigator.SessionChanged += OnSessionChanged;

        RebuildNavigation();
        ShowChat();
    }

    /// <summary>The two-pane root.</summary>
    public Control Root { get; }

    /// <summary>The right-side view host.</summary>
    public ContentControl ViewHost { get; }

    /// <summary>The live chat page, when the chat view is active (tests).</summary>
    internal ChatPageView? ChatPage => _chatPage;

    /// <summary>The sessions list (tests).</summary>
    internal ListBox SessionsList => _sessionsList;

    /// <summary>The "By date" toggle button (tests).</summary>
    internal Button ByDateButton => _byDateButton;

    /// <summary>The "By workspace" toggle button (tests).</summary>
    internal Button ByWorkspaceButton => _byWorkspaceButton;

    /// <summary>The "New Chat" button (tests).</summary>
    internal Button NewChatButton => _newChatButton;

    /// <summary>The "Models" button (tests).</summary>
    internal Button ModelsButton => _modelsButton;

    /// <summary>The "Providers" button (tests).</summary>
    internal Button ProvidersButton => _providersButton;

    /// <summary>The active group-mode button's icon tint (tests).</summary>
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
        RebuildNavigation();
    }

    /// <summary>Builds an icon + label button content (left-aligned row).</summary>
    private static StackPanel BuildToggleContent(MaterialIconKind kind, string label) =>
        new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new MaterialIcon { Kind = kind, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
            },
        };

    /// <summary>
    /// Styles a sidebar nav button (New Chat / Models / Providers) as a flat
    /// row: transparent by default so it reads like the session list items,
    /// with a subtle background only while hovered.
    /// </summary>
    private static void StyleAsFlatNavButton(Button button)
    {
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(6, 3);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
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
    internal DockPanel BuildSessionRow(NavModel.Entry entry)
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

        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2) };
        DockPanel.SetDock(title, Dock.Left);
        DockPanel.SetDock(menu, Dock.Right);
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
    internal DockPanel BuildWorkspaceRow(NavModel.Entry entry)
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

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(menu, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(menu);
        return row;
    }

    /// <summary>
    /// Switches the row into edit mode: the whole row becomes the rename
    /// field (title and menu hidden so the box fills the row width).
    /// </summary>
    internal static void BeginRename(NavModel.Entry entry, TextBlock title, TextBox renameBox, EllipsisMenu menu)
    {
        title.IsVisible = false;
        menu.IsVisible = false;
        renameBox.Text = entry.Title;
        renameBox.IsVisible = true;
        renameBox.Focus();
        renameBox.CaretIndex = renameBox.Text?.Length ?? 0;
    }

    /// <summary>
    /// Ends edit mode. When <paramref name="commit"/> is true (Enter / blur)
    /// the new title is persisted if it changed; Esc cancels without
    /// writing. Either way the row returns to its normal display.
    /// </summary>
    internal void EndRename(NavModel.Entry entry, TextBlock title, TextBox renameBox, EllipsisMenu menu, bool commit)
    {
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
        _postToUi(RebuildNavigation);
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
        var wasActive = _navigator.Current.State.SessionId == id;
        WorkspaceSessionStore.DeleteSession(id);
        if (wasActive)
            _postToUi(() => _ = _navigator.NavigateToNewAsync());
        else
            _postToUi(RebuildNavigation);
    }

    internal void NewSessionInWorkspace(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return;
        _postToUi(() => _ = _navigator.NavigateToNewAsync(cwd));
    }

    internal void DeleteWorkspaceRow(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return;
        var wasActive = Path.GetFullPath(_navigator.Current.Cwd)
            .Equals(Path.GetFullPath(cwd), StringComparison.OrdinalIgnoreCase);
        WorkspaceSessionStore.DeleteWorkspace(cwd);
        if (wasActive)
            _postToUi(() => _ = _navigator.NavigateToNewAsync());
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
            _sessionsList.ItemsSource = _entries;
            _sessionsList.SelectedIndex = NavModel.IndexForActive(
                _entries, _navigator.Current.State.SessionId);

            UpdateGroupToggle();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>
    /// Applies the group-mode toggle's selected state: the active button is
    /// filled with the accent color and its icon turns white (unmistakable),
    /// the inactive one stays transparent with a secondary icon.
    /// </summary>
    private void UpdateGroupToggle()
    {
        var dateActive = _groupMode == NavModel.GroupMode.ByDate;
        var wsActive = _groupMode == NavModel.GroupMode.ByWorkspace;
        ApplyToggle(_byDateButton, dateActive);
        ApplyToggle(_byWorkspaceButton, wsActive);
    }

    private static void ApplyToggle(Button button, bool active)
    {
        if (button.Content is not MaterialIcon icon) return;
        button.Background = active ? AvaloniaTheme.Accent : Brushes.Transparent;
        button.BorderBrush = active ? AvaloniaTheme.Accent : AvaloniaTheme.ControlBorder;
        button.BorderThickness = new Thickness(active ? 0 : 1);
        icon.Foreground = active ? AvaloniaTheme.AccentText : AvaloniaTheme.TextSecondary;
    }

    private void OnSessionSelection()
    {
        if (_rebuilding) return;
        if (_sessionsList.SelectedItem is not NavModel.Entry entry) return;

        // Workspace rows are group titles, not clickable sessions. Clear the
        // highlight synchronously so it never visually sticks; the row's ⋯
        // menu stays usable because the container is not disabled.
        if (entry.Kind == NavModel.Kind.Workspace)
        {
            _sessionsList.SelectedIndex = -1;
            return;
        }

        // Defer the session-resume dispatch out of the ListBox's
        // SelectionChanged dispatch. Navigating (→ SessionChanged →
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
                _sessionsList.SelectedIndex = -1;
                break;
            case NavModel.Kind.Session:
                ShowChat();
                if (entry.SessionId is { } id)
                    _ = _navigator.ResumeAsync(id);
                break;
        }
    }

    private void OnSessionChanged()
    {
        _dispatchToUi(() =>
        {
            if (_showingChat)
                ShowChat();
            RebuildNavigation();
        });
    }

    // ──────── View builders ────────

    private void ShowChat()
    {
        _chatPage?.Dispose();
        _chatPage = new ChatPageView(
            _navigator,
            _providers,
            _navigator.Current,
            pickFolder: PickFolderAsync,
            postToUi: _postToUi,
            dispatchToUi: _dispatchToUi);
        ViewHost.Content = _chatPage.Root;
        _showingChat = true;
        WatchSession(_navigator.Current);
        _postToUi(() => _chatPage?.PromptInput.FocusEditor());
    }

    /// <summary>
    /// Opens the platform folder picker via the ambient <see cref="TopLevel"/>'s
    /// storage provider. Works for both the desktop window lifetime and
    /// single-view lifetimes (Android, browser).
    /// </summary>
    private async Task<string?> PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(Root);
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

    private void ShowModels()
    {
        _showingChat = false;
        ViewHost.Content = new ModelsPage(_navigator.Current, _providers).Root;
    }

    private void ShowProviders()
    {
        _showingChat = false;
        ViewHost.Content = new ProvidersPage(
            _navigator.Current, _providers, owner: TopLevel.GetTopLevel(Root) as Window).Root;
    }

    public void Dispose()
    {
        _navigator.SessionChanged -= OnSessionChanged;
        if (_watchedSession is not null)
            _watchedSession.StateChanged -= OnSessionStateForNav;
        _chatPage?.Dispose();
        _chatPage = null;
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
