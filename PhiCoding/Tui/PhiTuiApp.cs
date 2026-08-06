using PhiCoding.Pages;
using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Sessions;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui;

/// <summary>
/// TUI shell over <see cref="ISessionNavigator"/>. Routes are resolved to
/// pages by a <see cref="PageRegistry"/> and mounted in a
/// <c>ContentSwitcher</c> page host; navigating (new / resume) rebuilds the
/// page for the new session. The layout skeleton stays fixed; each page owns
/// its own view, state, and interactions. Session teardown is owned by the
/// navigator, not the TUI.
/// </summary>
public sealed class PhiTuiApp
{
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;
    private readonly PageRegistry _pages;
    private ContentSwitcher? _pageHost;
    private IPage? _currentPage;

    public PhiTuiApp(ISessionNavigator navigator, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        _navigator = navigator;
        _providers = providers;
        _pages = new PageRegistry();

        // On navigation the navigator has already swapped the session; mount
        // the page for the new route.
        _navigator.RouteChanged += _ => MountPage();
    }

    /// <summary>Chat transcript of the current page (null on the landing page).</summary>
    public ChatTranscript? Transcript => (_currentPage as SessionPage)?.Transcript;

    /// <summary>Status bar of the current page (null on the landing page).</summary>
    public PhiStatusBar? StatusBar => (_currentPage as SessionPage)?.StatusBar;

    /// <summary>Suggestion strip of the current page.</summary>
    public SuggestionStrip? SuggestionStrip => (_currentPage as ChatScreen)?.SuggestionStrip;

    public (Visual Root, PromptEditor Editor) BuildRoot()
    {
        var page = MountPage();
        return (_pageHost!, ((ChatScreen)page).Editor);
    }

    public void Run()
    {
        using var terminal = Terminal.Open();
        var (root, _) = BuildRoot();
        // ToastHost overlays transient notifications (used by
        // SelectionCopyHost to confirm auto-copies); SelectionCopyHost wires
        // mouse drag-select / double-click → clipboard auto-copy.
        var toastHost = new ToastHost(new SelectionCopyHost(root));
        // Workaround for a XenoAtom.Terminal.UI 3.8.1 ToastHost bug: without
        // it, a toast shown after the previous one fully expired is dismissed
        // instantly. Remove when the upstream fix ships and NuGet is bumped.
        ToastHostSentinel.Install(toastHost);
        Terminal.Run(toastHost, () => TerminalLoopResult.Continue);
    }

    /// <summary>
    /// Resolves the page for the current route, builds it, and mounts it in
    /// the page host (creating the host on the first call). The page marks
    /// its editor with <c>AutoFocus</c>, so the library's
    /// <c>EnsureFocusInScope</c> restores focus after a child swap.
    /// </summary>
    private IPage MountPage()
    {
        var page = _pages.Resolve(_navigator.Route, _navigator, _providers);
        _currentPage = page;

        if (_pageHost is null)
        {
            _pageHost = new ContentSwitcher(page.Build())
            {
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Stretch,
            };
        }
        else
        {
            _pageHost.Children.Clear();
            _pageHost.Children.Add(page.Build());
        }

        return page;
    }
}
