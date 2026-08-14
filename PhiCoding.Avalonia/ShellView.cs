using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using PhiCoding.Avalonia.Components;
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
        newChatButton.Click += (_, _) => _postToUi(() =>
        {
            ShowChat();
            _ = _navigator.NavigateToNewAsync();
        });

        _byDateButton = new Button { Content = BuildToggleContent(MaterialIconKind.CalendarMonth, "By date"), FontSize = 12, Padding = new Thickness(6, 2) };
        _byWorkspaceButton = new Button { Content = BuildToggleContent(MaterialIconKind.Folder, "By workspace"), FontSize = 12, Padding = new Thickness(6, 2) };
        _byDateButton.Click += (_, _) => SetGroupMode(NavModel.GroupMode.ByDate);
        _byWorkspaceButton.Click += (_, _) => SetGroupMode(NavModel.GroupMode.ByWorkspace);

        var toggleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(8, 0),
            Children = { _byDateButton, _byWorkspaceButton },
        };

        _sessionsList = new ListBox
        {
            Margin = new Thickness(8, 4),
            ItemTemplate = new FuncDataTemplate<NavModel.Entry>((entry, _) =>
            {
                // ItemsSource swaps on a live ListBox recycle containers and
                // may briefly invoke the template with null data; skip it.
                if (entry is null) return null!;
                var isHeader = entry.Kind is NavModel.Kind.Workspace;
                var text = new TextBlock
                {
                    Text = isHeader ? entry.Title.ToUpperInvariant() : entry.Title,
                    FontSize = isHeader ? 11 : 13,
                    FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = isHeader ? new Thickness(4, 10, 0, 2) : new Thickness(4, 0),
                };
                if (isHeader)
                    text.Foreground = AvaloniaTheme.TextSecondary;
                return text;
            }),
        };
        _sessionsList.SelectionChanged += (_, _) => OnSessionSelection();

        var modelsButton = new Button
        {
            Content = BuildToggleContent(MaterialIconKind.CubeOutline, "Models"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        modelsButton.Click += (_, _) => _postToUi(ShowModels);
        var providersButton = new Button
        {
            Content = BuildToggleContent(MaterialIconKind.TuneVariant, "Providers"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        providersButton.Click += (_, _) => _postToUi(ShowProviders);

        var footer = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(8),
            Children = { modelsButton, providersButton },
        };

        // Grid with Auto rows for chrome and a star row for the session
        // list, so the ListBox fills the remaining height and scrolls
        // instead of overflowing.
        var leftPane = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
        };
        Grid.SetRow(newChatButton, 0);
        Grid.SetRow(toggleRow, 1);
        Grid.SetRow(_sessionsList, 2);
        Grid.SetRow(footer, 3);
        leftPane.Children.Add(newChatButton);
        leftPane.Children.Add(toggleRow);
        leftPane.Children.Add(_sessionsList);
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

            _byDateButton.FontWeight = _groupMode == NavModel.GroupMode.ByDate
                ? FontWeight.Bold : FontWeight.Normal;
            _byWorkspaceButton.FontWeight = _groupMode == NavModel.GroupMode.ByWorkspace
                ? FontWeight.Bold : FontWeight.Normal;
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void OnSessionSelection()
    {
        if (_rebuilding) return;
        if (_sessionsList.SelectedItem is not NavModel.Entry entry) return;

        // Defer the whole selection handling out of the ListBox's
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
                // Group header: not a real selection; snap back to the
                // active row so the header doesn't stay highlighted.
                _sessionsList.SelectedIndex = NavModel.IndexForActive(
                    _entries, _navigator.Current.State.SessionId);
                break;
            case NavModel.Kind.NewChat:
                ShowChat();
                _ = _navigator.NavigateToNewAsync();
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
