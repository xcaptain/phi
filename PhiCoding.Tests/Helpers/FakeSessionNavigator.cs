using PhiCoding.Sessions;

namespace PhiCoding.Tests.Helpers;

/// <summary>
/// In-memory <see cref="ISessionNavigator"/> for TUI tests. Wraps a single
/// <see cref="MockSession"/> (or any <see cref="ISession"/>); navigation
/// simply records the latest route and re-raises <see cref="RouteChanged"/>.
/// </summary>
public sealed class FakeSessionNavigator : ISessionNavigator
{
    public FakeSessionNavigator(ISession session, SessionRoute? route = null)
    {
        Current = session;
        Route = route ?? new NewSessionRoute();
    }

    public ISession Current { get; private set; }

    public SessionRoute Route { get; private set; }

    public event Action<SessionRoute>? RouteChanged;

    /// <summary>Last route passed to <see cref="NavigateAsync"/>, or null.</summary>
    public SessionRoute? LastRoute { get; private set; }

    public IReadOnlyList<SessionRecord> RecentSessions { get; set; } = [];

    public Task NavigateAsync(SessionRoute route)
    {
        LastRoute = route;
        Route = route;
        RouteChanged?.Invoke(route);
        return Task.CompletedTask;
    }

    public IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7) => RecentSessions;

    public void Dispose() { }
}
