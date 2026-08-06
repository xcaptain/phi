using PhiCoding.Tui.Pages;
using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Sessions;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui;

/// <summary>
/// TUI shell over <see cref="ISessionNavigator"/>. The root is a
/// <c>ComputedVisual</c> whose builder resolves the current
/// <see cref="AppRoute"/> to a page and returns its visual tree; navigation
/// simply flips a local <see cref="State{AppRoute}"/> and the library marks
/// the host dirty and swaps the child — the ControlsDemo multi-page pattern
/// (reactive state → builder rebuild → child swap). Each page owns its own
/// view, state, and interactions; session teardown is owned by the navigator,
/// not the TUI.
/// <para>
/// The <see cref="State{AppRoute}"/> lives only inside the TUI (created here,
/// written by the <see cref="ISessionNavigator.RouteChanged"/> handler on the
/// UI thread, read by the <c>ComputedVisual</c> builder during render) so it
/// never crosses a non-UI thread boundary — that is what bit us when the
/// navigator first exposed <see cref="State{T}"/> directly.
/// </para>
/// </summary>
public sealed class PhiTuiApp
{
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;
    private readonly PageRegistry _pages;
    private readonly State<AppRoute> _routeState;

    public PhiTuiApp(ISessionNavigator navigator, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        _navigator = navigator;
        _providers = providers;
        _pages = new PageRegistry();
        _routeState = new State<AppRoute>(navigator.Route);

        // The navigator has already swapped the session; flip the route state
        // and the page host rebuilds the new page on the next render.
        _navigator.RouteChanged += route => _routeState.Value = route;
    }

    /// <summary>The page host: a <c>ComputedVisual</c> that renders the page
    /// for the current route and swaps it on navigation.</summary>
    public Visual BuildRoot()
        => new ComputedVisual(BuildRoutedContent)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };

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
    /// <c>ComputedVisual</c> builder: reads <see cref="_routeState"/> (a
    /// tracked read — the library marks the host dirty and re-invokes this
    /// builder when navigation changes it), then resolves that route to a
    /// page and returns its visual tree.
    /// </summary>
    private Visual BuildRoutedContent()
    {
        var route = _routeState.Value;
        var page = _pages.Resolve(route, _navigator, _providers);
        return page.Build();
    }
}
