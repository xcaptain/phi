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

        Nav = new NavigationView { PaneWidth = 220 };
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
    // Fluent "regular" (outline-style) glyphs, matching the gallery demo's
    // unfilled icon look instead of chunky solid shapes.

    private static readonly PathGeometry AddIcon =
        PathGeometry.Parse("M14.5,13 L14.5,3.75378577 C14.5,3.33978577 14.164,3.00378577 13.75,3.00378577 C13.336,3.00378577 13,3.33978577 13,3.75378577 L13,13 L3.75387573,13 C3.33987573,13 3.00387573,13.336 3.00387573,13.75 C3.00387573,14.164 3.33987573,14.5 3.75387573,14.5 L13,14.5 L13,23.7523651 C13,24.1663651 13.336,24.5023651 13.75,24.5023651 C14.164,24.5023651 14.5,24.1663651 14.5,23.7523651 L14.5,14.5 L23.7498262,14.5030754 C24.1638262,14.5030754 24.4998262,14.1670754 24.4998262,13.7530754 C24.4998262,13.3390754 24.1638262,13.0030754 23.7498262,13.0030754 L14.5,13 Z");
    private static readonly PathGeometry ChatIcon =
        PathGeometry.Parse("M14.0038862,2.5 C20.3551608,2.5 25.5038862,7.64872538 25.5038862,14 C25.5038862,20.3512746 20.3551608,25.5 14.0038862,25.5 C12.0828262,25.5 10.2289934,25.0277557 8.57453454,24.1400553 L4.06949813,25.4927799 C3.40830889,25.6913248 2.71135661,25.3162775 2.51281166,24.6550883 C2.44236748,24.4204969 2.44239769,24.1703786 2.5128929,23.935823 L3.86597493,19.4333464 C2.97690068,17.777898 2.50388618,15.9226229 2.50388618,14 C2.50388618,7.64872538 7.65261155,2.5 14.0038862,2.5 Z M14.0038862,4 C8.48103868,4 4.00388618,8.4771525 4.00388618,14 C4.00388618,15.7703119 4.46384891,17.4718347 5.32571954,18.9725127 L5.48656853,19.2525809 L4.08316234,23.9225148 L8.75512584,22.5196672 L9.03501121,22.6802549 C10.5348218,23.5407899 12.2350195,24 14.0038862,24 C19.5267337,24 24.0038862,19.5228475 24.0038862,14 C24.0038862,8.4771525 19.5267337,4 14.0038862,4 Z M10.2538862,15.5 L14.7521489,15.5 C15.1663625,15.5 15.5021489,15.8357864 15.5021489,16.25 C15.5021489,16.6296958 15.219995,16.943491 14.8539195,16.9931534 L14.7521489,17 L10.2538862,17 C9.83967261,17 9.50388618,16.6642136 9.50388618,16.25 C9.50388618,15.8703042 9.78604006,15.556509 10.1521156,15.5068466 L10.2538862,15.5 L14.7521489,15.5 L10.2538862,15.5 Z M10.2538862,11 L17.7583662,11 C18.1725798,11 18.5083662,11.3357864 18.5083662,11.75 C18.5083662,12.1296958 18.2262123,12.443491 17.8601368,12.4931534 L17.7583662,12.5 L10.2538862,12.5 C9.83967261,12.5 9.50388618,12.1642136 9.50388618,11.75 C9.50388618,11.3703042 9.78604006,11.056509 10.1521156,11.0068466 L10.2538862,11 L17.7583662,11 L10.2538862,11 Z");
    private static readonly PathGeometry CubeIcon =
        PathGeometry.Parse("M11.7204 2.0565C11.9012 1.9832 12.1034 1.9832 12.2841 2.0565L21.5343 5.80757C21.8173 5.92235 22.0025 6.19727 22.0024 6.50269C22.0024 6.52588 22.0014 6.5489 21.9993 6.57168L21.9993 17.504C21.9993 17.8094 21.814 18.0843 21.531 18.1991L12.2911 21.9435C12.1932 21.9832 12.0918 22.0007 11.9925 21.9984C11.8967 21.9986 11.8005 21.9805 11.7092 21.9435L2.46929 18.1991C2.18621 18.0843 2.00098 17.8094 2.00098 17.504L2.00098 6.55341C2.00098 6.53749 2.00147 6.52169 2.00245 6.50602L2.00244 6.50268C2.00241 6.19728 2.18757 5.92235 2.47059 5.80758L11.7204 2.0565ZM12.7418 20.1424L20.4993 16.9986L20.4993 7.61708L12.7499 10.7576L12.7418 20.1424ZM3.50098 7.61522L3.50098 16.9986L11.2418 20.1356L11.2499 10.7557L3.50098 7.61522ZM12.0023 3.56085L4.74871 6.50238L12.0023 9.44206L19.2561 6.50238L12.0023 3.56085Z");
    private static readonly PathGeometry SettingsIcon =
        PathGeometry.Parse("M14 9.50006C11.5147 9.50006 9.5 11.5148 9.5 14.0001C9.5 16.4853 11.5147 18.5001 14 18.5001C15.3488 18.5001 16.559 17.9066 17.3838 16.9666C18.0787 16.1746 18.5 15.1365 18.5 14.0001C18.5 13.5401 18.431 13.0963 18.3028 12.6784C17.7382 10.8381 16.0253 9.50006 14 9.50006ZM11 14.0001C11 12.3432 12.3431 11.0001 14 11.0001C15.6569 11.0001 17 12.3432 17 14.0001C17 15.6569 15.6569 17.0001 14 17.0001C12.3431 17.0001 11 15.6569 11 14.0001Z M21.7093 22.3948L19.9818 21.6364C19.4876 21.4197 18.9071 21.4515 18.44 21.7219C17.9729 21.9924 17.675 22.4693 17.6157 23.0066L17.408 24.8855C17.3651 25.273 17.084 25.5917 16.7055 25.682C14.9263 26.1061 13.0725 26.1061 11.2933 25.682C10.9148 25.5917 10.6336 25.273 10.5908 24.8855L10.3834 23.0093C10.3225 22.4731 10.0112 21.9976 9.54452 21.7281C9.07783 21.4586 8.51117 21.4269 8.01859 21.6424L6.29071 22.4009C5.93281 22.558 5.51493 22.4718 5.24806 22.1859C4.00474 20.8536 3.07924 19.2561 2.54122 17.5137C2.42533 17.1384 2.55922 16.7307 2.8749 16.4977L4.40219 15.3703C4.83721 15.0501 5.09414 14.5415 5.09414 14.0007C5.09414 13.4598 4.83721 12.9512 4.40162 12.6306L2.87529 11.5051C2.55914 11.272 2.42513 10.8638 2.54142 10.4882C3.08038 8.74734 4.00637 7.15163 5.24971 5.82114C5.51684 5.53528 5.93492 5.44941 6.29276 5.60691L8.01296 6.36404C8.50793 6.58168 9.07696 6.54881 9.54617 6.27415C10.0133 6.00264 10.3244 5.52527 10.3844 4.98794L10.5933 3.11017C10.637 2.71803 10.9245 2.39704 11.3089 2.31138C12.19 2.11504 13.0891 2.01071 14.0131 2.00006C14.9147 2.01047 15.8128 2.11485 16.6928 2.31149C17.077 2.39734 17.3643 2.71823 17.4079 3.11017L17.617 4.98937C17.7116 5.85221 18.4387 6.50572 19.3055 6.50663C19.5385 6.507 19.769 6.45838 19.9843 6.36294L21.7048 5.60568C22.0626 5.44818 22.4807 5.53405 22.7478 5.81991C23.9912 7.1504 24.9172 8.74611 25.4561 10.487C25.5723 10.8623 25.4386 11.2703 25.1228 11.5035L23.5978 12.6297C23.1628 12.95 22.9 13.4586 22.9 13.9994C22.9 14.5403 23.1628 15.0489 23.5988 15.3698L25.1251 16.4965C25.441 16.7296 25.5748 17.1376 25.4586 17.5131C24.9198 19.2536 23.9944 20.8492 22.7517 22.1799C22.4849 22.4657 22.0671 22.5518 21.7093 22.3948ZM16.263 22.1966C16.4982 21.4685 16.9889 20.8288 17.6884 20.4238C18.5702 19.9132 19.6536 19.8547 20.5841 20.2627L21.9281 20.8526C22.791 19.8538 23.4593 18.7013 23.8981 17.4552L22.7095 16.5778L22.7086 16.5771C21.898 15.98 21.4 15.0277 21.4 13.9994C21.4 12.9719 21.8974 12.0195 22.7073 11.4227L22.7085 11.4218L23.8957 10.545C23.4567 9.2988 22.7881 8.14636 21.9248 7.1477L20.5922 7.73425L20.5899 7.73527C20.1844 7.91463 19.7472 8.00722 19.3039 8.00663C17.6715 8.00453 16.3046 6.77431 16.1261 5.15465L16.1259 5.15291L15.9635 3.69304C15.3202 3.57328 14.6677 3.50872 14.013 3.50017C13.3389 3.50891 12.6821 3.57367 12.0377 3.69328L11.8751 5.15452C11.7625 6.16272 11.1793 7.05909 10.3019 7.56986C9.41937 8.0856 8.34453 8.14844 7.40869 7.73694L6.07273 7.14893C5.20949 8.14751 4.54092 9.29983 4.10196 10.5459L5.29181 11.4233C6.11115 12.0269 6.59414 12.9837 6.59414 14.0007C6.59414 15.0173 6.11142 15.9742 5.29237 16.5776L4.10161 17.4566C4.54002 18.7044 5.2085 19.8585 6.07205 20.8587L7.41742 20.2682C8.34745 19.8613 9.41573 19.9215 10.2947 20.4292C11.174 20.937 11.7593 21.832 11.8738 22.84L11.8744 22.8445L12.0362 24.3088C13.3326 24.5638 14.6662 24.5638 15.9626 24.3088L16.1247 22.8418C16.1491 22.6217 16.1955 22.4055 16.263 22.1966Z");

    private static PathGeometry IconFor(DeskNavModel.Kind kind) => kind switch
    {
        DeskNavModel.Kind.NewChat => AddIcon,
        DeskNavModel.Kind.Session => ChatIcon,
        DeskNavModel.Kind.Models => CubeIcon,
        DeskNavModel.Kind.Providers => SettingsIcon,
        _ => ChatIcon,
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