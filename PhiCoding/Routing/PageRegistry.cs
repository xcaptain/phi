using System.Diagnostics.CodeAnalysis;
using PhiCoding.Pages;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Routing;

/// <summary>
/// The routing algorithm: maps an <see cref="AppRoute"/> to the page that
/// renders it. Mirrors the ControlsDemo's "find the selected demo → build its
/// page" resolution, but type-safe and explicit (no reflection). Adding a page
/// means adding a route family here and a page class.
/// </summary>
[SuppressMessage("Performance", "CA1822", Justification = "Service facade; instance members stay swappable/injectable")]
public sealed class PageRegistry
{
    /// <summary>
    /// Resolves the page for a route, constructing it with the live session
    /// (already hydrated by the navigator), the navigator (for navigation),
    /// and the provider manager (for <c>/connect</c>, <c>/models</c>).
    /// Throws <see cref="NotSupportedException"/> for unknown routes.
    /// </summary>
    public IPage Resolve(AppRoute route, ISessionNavigator navigator, ProviderManager providers) =>
        route switch
        {
            ChatRoute(NewSessionRequest) => new NewSessionPage(navigator.Current, navigator, providers),
            ChatRoute(ExistingSessionRequest) => new SessionPage(navigator.Current, navigator, providers),
            _ => throw new NotSupportedException($"No page registered for route {route}"),
        };
}
