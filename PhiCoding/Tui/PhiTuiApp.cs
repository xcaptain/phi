using PhiCoding.Tui.Pages;
using PhiCoding.Tui.Components;
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
/// <c>ComputedVisual</c> page host driven by a local <see cref="State{AppRoute}"/>;
/// this mirrors the ControlsDemo's multi-page pattern (reactive state →
/// builder rebuild → library swaps the child). The layout skeleton stays
/// fixed; each page owns its own view, state, and interactions. Session
/// teardown is owned by the navigator, not the TUI.
/// <para>
/// The <see cref="State{AppRoute}"/> lives only inside the TUI (created
/// here, written by the <see cref="ISessionNavigator.RouteChanged"/> handler
/// on the UI thread, read by the <c>ComputedVisual</c> builder during render)
/// so it never crosses a non-UI thread boundary — that is what bit us when
/// the navigator first exposed <see cref="State{T}"/> directly.
/// </para>
/// </summary>
public sealed class PhiTuiApp
{
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;
    private readonly PageRegistry _pages;
    private IPage? _currentPage;
    private Visual? _currentPageRoot;
    private readonly State<AppRoute> _routeState;

    public PhiTuiApp(ISessionNavigator navigator, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        _navigator = navigator;
        _providers = providers;
        _pages = new PageRegistry();
        _routeState = new State<AppRoute>(navigator.Route);

        // On navigation the navigator has already swapped the session. Cache
        // the new page eagerly so test-facing properties (Transcript, etc.)
        // are fresh without a render, and mark the host dirty so the next
        // render swaps its child.
        _navigator.RouteChanged += route =>
        {
            var (page, root) = ResolveAndBuild(route);
            _currentPage = page;
            _currentPageRoot = root;
            _routeState.Value = route;
        };
    }

    /// <summary>Chat transcript of the current page (null on the landing page).</summary>
    public ChatTranscript? Transcript => (_currentPage as SessionPage)?.Transcript;

    /// <summary>Status bar of the current page (null on the landing page).</summary>
    public PhiStatusBar? StatusBar => (_currentPage as SessionPage)?.StatusBar;

    /// <summary>Suggestion strip of the current page.</summary>
    public SuggestionStrip? SuggestionStrip => (_currentPage as SessionPage)?.Input.SuggestionStrip;

    public Visual BuildRoot()
    {
        var (page, root) = ResolveAndBuild(_navigator.Route);
        _currentPage = page;
        _currentPageRoot = root;
        return new ComputedVisual(BuildRoutedContent)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };
    }

    public void Run()
    {
        using var terminal = Terminal.Open();
        var root = BuildRoot();
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
    /// Resolves the page for a route, builds it, and caches it in
    /// <see cref="_currentPage"/>. Always reads the navigator's current
    /// session so a new-session page is bound to the fresh session and an
    /// existing-session page is bound to the resumed one.
    /// </summary>
    private (IPage Page, Visual Root) ResolveAndBuild(AppRoute route)
    {
        var page = _pages.Resolve(route, _navigator, _providers);
        var root = page.Build();
        return (page, root);
    }

    /// <summary>
    /// <c>ComputedVisual</c> builder. Reads <see cref="_routeState"/> so the
    /// library marks the host dirty on navigation and re-invokes this builder
    /// to swap the child. Returns the cached page so the freshly bound
    /// transcript / status / editor flow through the same instance the
    /// RouteChanged handler cached.
    /// </summary>
    private Visual BuildRoutedContent()
    {
        _ = _routeState.Value;
        return _currentPageRoot ?? ResolveAndBuild(_navigator.Route).Item2;
    }
}