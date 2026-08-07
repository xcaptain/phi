using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using PhiCoding.Desk.Components;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Desk;

/// <summary>
/// The two-pane shell: a collapsible <see cref="NavigationView"/> on the
/// left (New Chat, Sessions, footer Models/Providers) and a single
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

    private DeskChatPage? _chatPage;
    private bool _showingChat;
    private bool _rebuilding;

    public DeskShell(
        ISessionNavigator navigator,
        ProviderManager providers,
        Action<Action>? dispatchToUi = null,
        Action<Action>? postToUi = null)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        _navigator = navigator;
        _providers = providers;
        _dispatchToUi = dispatchToUi ?? (action => action());
        _postToUi = postToUi ?? (action => action());

        Nav = new NavigationView { PaneWidth = 240 };
        // The content region is a single host we drive directly — this
        // avoids NavigationView's per-item content caching, so switching
        // views (chat / models / providers) always swaps the live page.
        Nav.ContentSelector = _ => ViewHost;
        Nav.FooterContentSelector = _ => ViewHost;
        Nav.SelectionChanged += OnNavSelection;

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

    public void RebuildNavigation()
    {
        _rebuilding = true;
        try
        {
            var entries = DeskNavModel.BuildMainEntries(_navigator.ListRecentSessions(7));
            Nav.Items(
                entries,
                e => e.Title,
                icon: e => IconFor(e.Kind),
                content: _ => ViewHost,
                kind: e => e.Kind == DeskNavModel.Kind.Header ? NavigationItemKind.Header : NavigationItemKind.Item,
                keySelector: e => e);
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

    private static readonly PathGeometry PlusIcon =
        PathGeometry.Parse("M10 4h4v6h6v4h-6v6h-4v-6H4v-4h6z");
    private static readonly PathGeometry BubbleIcon =
        PathGeometry.Parse("M12 4c5 0 9 3.6 9 8s-4 8-9 8c-1 0-1.9-.1-2.8-.4L5 21l1.2-3.4C4.8 16.1 4 14.2 4 12c0-4.4 4-8 8-8z");
    private static readonly PathGeometry CubeIcon =
        PathGeometry.Parse("M12 2l8 4.2v11.6L12 22l-8-4.2V6.2z");
    private static readonly PathGeometry PlugIcon =
        PathGeometry.Parse("M7 3h3v6h4V3h3v7.5c0 1.8-1.3 3.3-3 3.7V21h-4v-6.8c-1.7-.4-3-1.9-3-3.7z");

    private static PathGeometry IconFor(DeskNavModel.Kind kind) => kind switch
    {
        DeskNavModel.Kind.NewChat => PlusIcon,
        DeskNavModel.Kind.Session => BubbleIcon,
        DeskNavModel.Kind.Models => CubeIcon,
        DeskNavModel.Kind.Providers => PlugIcon,
        _ => BubbleIcon,
    };

    // ──────── Selection → view dispatch ────────

    /// <summary>Invokes the same dispatch a user selection would trigger (tests).</summary>
    internal void Select(DeskNavModel.Kind kind, string? sessionId = null)
        => OnNavSelection(new DeskNavModel.Entry(kind, "test", sessionId));

    private void OnNavSelection(object? item)
    {
        if (_rebuilding) return;
        if (item is not DeskNavModel.Entry entry) return;

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
        _chatPage = new DeskChatPage(_navigator, _providers, _navigator.Current);
        ViewHost.Content = _chatPage.Root;
        _showingChat = true;
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
        _chatPage?.Dispose();
        _chatPage = null;
    }
}