using PhiCoding.Routing;
using PhiCoding.Sessions;

namespace PhiCoding.Tests.Helpers;

/// <summary>
/// In-memory <see cref="ISessionNavigator"/> for TUI tests. Wraps a single
/// <see cref="MockSession"/> (or any <see cref="ISession"/>); navigation
/// simply records the latest route and re-raises <see cref="RouteChanged"/>.
/// </summary>
public sealed class FakeSessionNavigator : ISessionNavigator
{
    public FakeSessionNavigator(ISession session, AppRoute? route = null)
    {
        Current = session;
        Route = route ?? new ChatRoute(new NewSessionRequest());
    }

    public ISession Current { get; private set; }

    public AppRoute Route { get; private set; }

    public event Action<AppRoute>? RouteChanged;

    /// <summary>Last route passed to <see cref="NavigateAsync"/>, or null.</summary>
    public AppRoute? LastRoute { get; private set; }

    public IReadOnlyList<SessionRecord> RecentSessions { get; set; } = [];

    /// <summary>Backing for <see cref="SetPendingSubmission"/>/<see cref="TakePendingSubmission"/>.</summary>
    public string? PendingSubmission { get; private set; }

    public Task NavigateAsync(AppRoute route)
    {
        LastRoute = route;
        Route = route;
        RouteChanged?.Invoke(route);
        return Task.CompletedTask;
    }

    public void SetPendingSubmission(string text) => PendingSubmission = text;

    public string? TakePendingSubmission()
    {
        var pending = PendingSubmission;
        PendingSubmission = null;
        return pending;
    }

    public IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7) => RecentSessions;

    public void Dispose() { }
}
