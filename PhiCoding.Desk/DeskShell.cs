using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using PhiCoding.Desk.Components;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Desk;

/// <summary>
/// The two-pane shell: a collapsible <see cref="NavigationView"/> on the
/// left (New Chat, an optional Sessions region with a "By date / By
/// workspace" toggle, footer Models/Providers) and a single
/// <see cref="ContentControl"/> host on the right that displays whichever
/// view is active. Extracted from <see cref="PhiDeskApp"/> so the
/// navigation logic can be exercised without a live window.
/// </summary>
internal sealed class DeskShell : IDisposable
{
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;
    private readonly Action<Action> _dispatchToUi;
    private readonly Action<Action> _postToUi;
    private readonly Window? _owner;

    private DeskChatPage? _chatPage;
    private bool _showingChat;
    private bool _rebuilding;
    private ISession? _watchedSession;
    private bool _wasPersisted;
    private string? _lastTitle;
    private DeskNavModel.GroupMode _groupMode = DeskNavModel.GroupMode.ByWorkspace;
    private DeskNavModel.PaneMode _paneDisplayMode = DeskNavModel.PaneMode.Expanded;
    private DispatcherTimer _paneWatchTimer = null!;
    private Aprillz.MewUI.Controls.PaneDisplayMode _lastObservedPaneMode =
        Aprillz.MewUI.Controls.PaneDisplayMode.Auto;

    public DeskShell(
        ISessionNavigator navigator,
        ProviderManager providers,
        Window? owner = null,
        Action<Action>? dispatchToUi = null,
        Action<Action>? postToUi = null)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        _navigator = navigator;
        _providers = providers;
        _owner = owner;
        _dispatchToUi = dispatchToUi ?? (action => action());
        _postToUi = postToUi ?? (action => action());

        Nav = new NavigationView { PaneWidth = 220 };
        // Install the pane row template up front, before any items source
        // populates containers. NavigationView.Items would rewrite this
        // template on every call, and swapping the template poisons the
        // presenter's recycled containers (a Border built by the old
        // template carries the wrong child type into the new template's
        // bind/unbind). Setting it once here and only refreshing the data
        // source on rebuilds keeps the container structure stable.
        Nav.Pane.ItemTemplate = BuildPaneItemTemplate();
        // The content region is a single host we drive directly — this
        // avoids NavigationView's per-item content caching, so switching
        // views (chat / models / providers) always swaps the live page.
        Nav.ContentSelector = _ => ViewHost;
        Nav.FooterContentSelector = _ => ViewHost;
        Nav.SelectionChanged += OnNavSelection;
        // Track the pane's expanded/compact state. Compact hides the
        // sessions region entirely; only NewChat (+ footer) remains.
        // Aprillz.MewUI 0.19.x NavigationView is sealed and doesn't expose
        // a PaneDisplayChanged event, so we poll the public PaneDisplayMode
        // property on a low-frequency dispatcher timer and rebuild the
        // nav when it changes.
        _paneWatchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150));
        _paneWatchTimer.Tick += OnPaneWatchTick;
        _paneWatchTimer.Start();

        _navigator.SessionChanged += OnSessionChanged;

        RebuildNavigation();
        ShowChat();
    }

    /// <summary>The navigation pane + content host.</summary>
    public NavigationView Nav { get; }

    /// <summary>The right-side view host.</summary>
    public ContentControl ViewHost { get; } = new();

    /// <summary>The live chat page, when the chat view is active (tests).</summary>
    internal DeskChatPage? ChatPage => _chatPage;

    /// <summary>The root element: the nav wrapped in a top separator border.</summary>
    public FrameworkElement Root { get; private set; } = null!;

    /// <summary>Builds the root border (nav + top separator).</summary>
    public FrameworkElement BuildRoot() => Root ??= new Border()
        .BorderThickness(new Thickness(0, 1, 0, 0))
        .WithTheme((t, b) => b.BorderBrush(t.Palette.WindowBackground.Lerp(t.Palette.ControlBorder, 0.45)))
        .Child(Nav);

    // ──────── Navigation model ────────

    private void OnPaneDisplayChanged()
    {
        // Map MewUI's PaneDisplayMode (Expanded/Compact/Minimal/Auto) to our
        // simple Expanded/Compact enum; we only honour the toggle between
        // those two and ignore Minimal/Auto.
        _paneDisplayMode = Nav.PaneDisplayMode == Aprillz.MewUI.Controls.PaneDisplayMode.Compact
            ? DeskNavModel.PaneMode.Compact
            : DeskNavModel.PaneMode.Expanded;
        RebuildNavigation();
    }

    private void OnPaneWatchTick()
    {
        var current = Nav.PaneDisplayMode;
        if (current == _lastObservedPaneMode) return;
        _lastObservedPaneMode = current;
        // Defer to the dispatcher to avoid re-entering RebuildNavigation
        // if we happen to tick from inside another rebuild.
        _dispatchToUi(OnPaneDisplayChanged);
    }

    /// <summary>
    /// Returns the current group mode (by date / by workspace). Tests may
    /// set this and call <see cref="RebuildNavigation"/> to exercise UI
    /// changes without needing real user clicks.
    /// </summary>
    internal DeskNavModel.GroupMode GroupMode
    {
        get => _groupMode;
        set { _groupMode = value; RebuildNavigation(); }
    }

    /// <summary>
    /// Returns the current pane mode (Expanded / Compact) as observed
    /// from the underlying <see cref="NavigationView"/>. Tests may read
    /// this to verify the watcher keeps the model in sync.
    /// </summary>
    internal DeskNavModel.PaneMode PaneMode => _paneDisplayMode;

    /// <summary>
    /// Invokes the same handler the pane-display watcher uses, so tests
    /// can simulate a user clicking the hamburger without waiting for the
    /// <see cref="DispatcherTimer"/> to tick.
    /// </summary>
    internal void SimulatePaneDisplayChange() => OnPaneDisplayChanged();

    public void RebuildNavigation()
    {
        _rebuilding = true;
        try
        {
            var entries = DeskNavModel.BuildMainEntries(
                WorkspaceSessionStore.ListAllSessions(7),
                _groupMode,
                _paneDisplayMode);
            // Populate the pane directly (ItemsSource + KindSelector) instead
            // of NavigationView.Items: the Items overload rewrites the pane
            // item template on every call. The custom template is installed
            // once in the constructor; here we only refresh the data source
            // so rows rebind with fresh values.
            Nav.Pane.ItemsSource = ItemsView.Create(entries, e => e.Title, keySelector: e => e);
            Nav.Pane.KindSelector = e => e is DeskNavModel.Entry entry
                && entry.Kind is DeskNavModel.Kind.Header
                    or DeskNavModel.Kind.Workspace
                    or DeskNavModel.Kind.ToggleRow
                    ? NavigationItemKind.Header
                    : NavigationItemKind.Item;
            Nav.FooterItems(
                DeskNavModel.BuildFooterEntries(),
                e => e.Title,
                icon: e => IconFor(e.Kind),
                content: _ => ViewHost,
                kind: e => NavigationItemKind.Item,
                keySelector: e => e);
            Nav.SelectedIndex = DeskNavModel.IndexForActive(entries, _navigator.Current.State.SessionId);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    // ──────── Nav icons ────────
    // The icons here are limited to the items that genuinely benefit from a
    // glyph: NewChat (a Plus), Models/Settings (footer). Session rows and
    // workspace headers are pure text — an icon in that tight column just
    // wastes space.

    private static readonly PathGeometry AddIcon =
        PathGeometry.Parse("M14.5,13 L14.5,3.75378577 C14.5,3.33978577 14.164,3.00378577 13.75,3.00378577 C13.336,3.00378577 13,3.33978577 13,3.75378577 L13,13 L3.75387573,13 C3.33987573,13 3.00387573,13.336 3.00387573,13.75 C3.00387573,14.164 3.33987573,14.5 3.75387573,14.5 L13,14.5 L13,23.7523651 C13,24.1663651 13.336,24.5023651 13.75,24.5023651 C14.164,24.5023651 14.5,24.1663651 14.5,23.7523651 L14.5,14.5 L23.7498262,14.5030754 C24.1638262,14.5030754 24.4998262,14.1670754 24.4998262,13.7530754 C24.4998262,13.3390754 24.1638262,13.0030754 23.7498262,13.0030754 L14.5,13 Z");
    private static readonly PathGeometry ChatIcon =
        PathGeometry.Parse("M14.0038862,2.5 C20.3551608,2.5 25.5038862,2.5 14.0038862,2.5 Z M14.0038862,4 C8.48103868,4 4.00388618,8.4771525 4.00388618,14 C4.00388618,15.7703119 4.46384891,17.4718347 5.32571954,18.9725127 L5.48656853,19.2525809 L4.08316234,23.9225148 L8.75512584,22.5196672 L9.03501121,22.6802549 C10.5348218,23.5407899 12.2350195,24 14.0038862,24 C19.5267337,24 24.0038862,19.5228475 24.0038862,14 C24.0038862,8.4771525 19.5267337,4 14.0038862,4 Z");
    private static readonly PathGeometry CubeIcon =
        PathGeometry.Parse("M11.7204 2.0565L21.5343 5.80757L21.9993 17.504L12.2911 21.9435L2.46929 18.1991L2.00098 6.55341L11.7204 2.0565Z");
    private static readonly PathGeometry SettingsIcon =
        PathGeometry.Parse("M14 9.50006C11.5147 9.50006 9.5 11.5148 9.5 14.0001C9.5 16.4853 11.5147 18.5001 14 18.5001C15.3488 18.5001 16.559 17.9066 17.3838 16.9666L21.7093 22.3948L19.9818 21.6364L19.4876 21.4197 18.9071 21.4515 18.44 21.7219C17.9729 21.9924 17.675 22.4693 17.6157 23.0066L17.408 24.8855L10.3844 4.98794L17.617 4.98937L21.7048 5.60568L11.7204 2.0565ZM12.0023 3.56085L4.74871 6.50238L12.0023 9.44206L19.2561 6.50238L12.0023 3.56085Z");

    private static PathGeometry? IconFor(DeskNavModel.Kind kind) => kind switch
    {
        DeskNavModel.Kind.NewChat => AddIcon,
        DeskNavModel.Kind.Models => CubeIcon,
        DeskNavModel.Kind.Providers => SettingsIcon,
        _ => null,
    };

    // ──────── Pane item template ────────

    /// <summary>
    /// Builds the pane row template. Every row is a <see cref="ContentControl"/>
    /// whose content depends on the entry kind: the toggle row renders a
    /// <see cref="ButtonGroup"/> (label + segmented toggle), everything else
    /// renders the standard icon + text row. Installed exactly once; the data
    /// source refresh alone is enough to rebind rows with fresh values.
    /// </summary>
    private DelegateTemplate<DeskNavModel.Entry> BuildPaneItemTemplate()
    {
        return new DelegateTemplate<DeskNavModel.Entry>(
            build: _ => new ContentControl(),
            bind: (element, entry, _, _) =>
            {
                var host = (ContentControl)element;
                if (entry.Kind == DeskNavModel.Kind.ToggleRow)
                {
                    host.Content = BuildToggleRowContent(entry.ToggleMode);
                    host.ToolTip((string?)null);
                    return;
                }

                host.Content = BuildNavRowContent(entry);
                // In the compact rail the label is hidden; surface the row
                // text as a tooltip so icons stay identifiable.
                host.ToolTip(!Nav.PaneShowsText && entry.Kind == DeskNavModel.Kind.NewChat
                    ? entry.Title
                    : null);
            },
            unbind: (element, _, _, _) => ((ContentControl)element).Content = null);
    }

    // ──────── Toggle row (date / workspace) ────────

    /// <summary>
    /// Builds the sessions header row: a left-aligned "会话" label and a
    /// right-docked <see cref="ButtonGroup"/> with Uniform sizing carrying
    /// two icon segments — a clock (by date) and a folder (by workspace).
    /// Clicking a segment switches the group mode and rebuilds the nav so
    /// the sessions list reorders.
    /// </summary>
    private DockPanel BuildToggleRowContent(DeskNavModel.GroupMode currentMode)
    {
        var group = new Aprillz.MewUI.Controls.ButtonGroup()
            .Sizing(Aprillz.MewUI.Controls.SegmentSizing.Uniform)
            .Items("By date", "By workspace")
            .ItemTemplate<string>(
                build: _ => new TextBlock
                {
                    IsHitTestVisible = false,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 13,
                },
                bind: (view, label, _, _) =>
                    ((TextBlock)view).Text = label == "By date" ? "⏰" : "📁")
            .PrepareContainer<string>(
                (seg, label, _) =>
                {
                    var mode = label == "By date"
                        ? DeskNavModel.GroupMode.ByDate
                        : DeskNavModel.GroupMode.ByWorkspace;
                    // Not IsCheckable: that would make each segment an
                    // independent self-toggling switch (Bold/Italic style),
                    // which fights the mutually-exclusive group mode. Keep
                    // segments as plain command buttons whose checked state
                    // is driven solely by _groupMode on each rebuild.
                    seg.IsChecked = mode == currentMode;
                    // The text is gone (icon-only), so surface the label as
                    // a tooltip.
                    seg.ToolTip(label);
                    seg.Click += () =>
                    {
                        if (_groupMode == mode) return;
                        _groupMode = mode;
                        RebuildNavigation();
                    };
                });

        // Label on the left, toggle on the right. The group is docked right
        // at its natural width (Uniform sizing gives both segments equal
        // width), leaving the row as "会话 … ⏰ | 📁".
        return new DockPanel()
            .LastChildFill(false)
            .Spacing(8)
            .Children(
                new Label().Text("会话").SemiBold(),
                group.DockRight());
    }

    /// <summary>
    /// Standard nav row: an icon host (only populated for NewChat) followed
    /// by the entry text. Headers are small uppercase text; items are 13px
    /// with a left indent. Mirrors the built-in NavigationList template.
    /// </summary>
    private StackPanel BuildNavRowContent(DeskNavModel.Entry entry)
    {
        var isHeader = entry.Kind is DeskNavModel.Kind.Header or DeskNavModel.Kind.Workspace;

        var iconHost = new ContentControl
        {
            Width = 16,
            Height = 16,
            BorderThickness = 0,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var geometry = IconFor(entry.Kind);
        if (geometry is not null)
        {
            var icon = new PathShape { Data = geometry, Stretch = Stretch.Uniform };
            icon.Bind(Shape.FillProperty, icon, TextElement.ForegroundProperty,
                (Color color) => (Brush)new SolidColorBrush(color));
            iconHost.Content = icon;
        }
        iconHost.IsVisible = geometry is not null;

        var label = new TextBlock().CenterVertical();
        label.IsVisible = Nav.PaneShowsText;
        if (isHeader)
        {
            label.Text = entry.Title.ToUpperInvariant();
            label.FontSize = 11;
            label.FontWeight = FontWeight.SemiBold;
        }
        else
        {
            label.Text = entry.Title;
            label.FontSize = 13;
            label.FontWeight = FontWeight.Normal;
        }

        return new StackPanel()
            .Horizontal()
            .Spacing(10)
            .CenterVertical()
            .Margin(isHeader ? new Thickness(0, 12, 0, 2) : new Thickness(12, 0, 0, 0))
            .Children(iconHost, label);
    }

    // ──────── Selection → view dispatch ────────

    /// <summary>Invokes the same dispatch a user selection would trigger (tests).</summary>
    internal void Select(DeskNavModel.Kind kind, string? sessionId = null)
        => OnNavSelection(new DeskNavModel.Entry(kind, "test", sessionId));

    private void OnNavSelection(object? item)
    {
        if (_rebuilding) return;
        if (item is not DeskNavModel.Entry entry) return;
        // The toggle row renders its own interactive content (the button);
        // its header kind isn't selectable, so it should never reach here,
        // but be defensive anyway.
        if (entry.Kind == DeskNavModel.Kind.ToggleRow) return;

        // Defer the whole selection handling out of the NavigationView's
        // SelectionChanged dispatch. Navigating (SwapAsync → SessionChanged →
        // RebuildNavigation) mutates the nav items + content host; running
        // that synchronously inside the very dispatch that is handling the
        // click re-enters the NavigationView and corrupts its pane/content
        // state — the chat page (editor) then fails to render. Posting to
        // the UI queue runs it after the dispatch settles.
        _postToUi(() => HandleSelection(entry));
    }

    private void HandleSelection(DeskNavModel.Entry entry)
    {
        switch (entry.Kind)
        {
            case DeskNavModel.Kind.Models:
                ShowModels();
                break;
            case DeskNavModel.Kind.Providers:
                ShowProviders();
                break;
            case DeskNavModel.Kind.NewChat:
                ShowChat();
                _ = _navigator.NavigateToNewAsync();
                break;
            case DeskNavModel.Kind.Session:
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
        _chatPage = new DeskChatPage(_navigator, _providers, _navigator.Current, _owner, _postToUi);
        ViewHost.Content = _chatPage.Root;
        _showingChat = true;
        WatchSession(_navigator.Current);
        // Focus the editor after the page attaches so Enter submits. This
        // matters after a workspace pick, which rebuilds the chat page while
        // the picker control still owns focus.
        _postToUi(() => _chatPage?.PromptInput.FocusEditor());
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
        // A fresh "New Chat" session isn't in the record-derived list until
        // its first message persists; when that happens, add it and move the
        // highlight off "New Chat" onto the session.
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
        ViewHost.Content = new ProvidersPage(_navigator.Current, _providers).Root;
    }

    public void Dispose()
    {
        _navigator.SessionChanged -= OnSessionChanged;
        if (_watchedSession is not null)
            _watchedSession.StateChanged -= OnSessionStateForNav;
        _paneWatchTimer.Stop();
        _paneWatchTimer.Dispose();
        _chatPage?.Dispose();
        _chatPage = null;
    }
}