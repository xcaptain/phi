using PhiCoding.Routing;

namespace PhiCoding.Sessions;

/// <summary>
/// Default <see cref="ISessionNavigator"/>: builds sessions through a
/// <see cref="CodingSessionFactory"/>, owns the current session's lifecycle,
/// and publishes route changes via <see cref="RouteChanged"/>.
/// <para>
/// A fresh session (<see cref="NewSessionRequest"/>) carries over the current
/// session's provider + model when one exists, so <c>/new</c> keeps the user
/// connected; at startup it uses the environment config's defaults.
/// </para>
/// </summary>
public sealed class SessionNavigator : ISessionNavigator
{
    private readonly CodingSessionFactory _factory;
    private readonly SessionConfig _env;
    private CodingSession? _current;
    private string? _pendingSubmission;

    public SessionNavigator(
        CodingSessionFactory factory, SessionConfig env, AppRoute initialRoute)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(initialRoute);
        _factory = factory;
        _env = env;
        // The initial navigation has no prior session to settle, so it is
        // performed synchronously here. May throw for an unknown id.
        _current = BuildSession(initialRoute);
        Route = initialRoute;
    }

    public ISession Current => _current!;

    public AppRoute Route { get; private set; }

    public event Action<AppRoute>? RouteChanged;

    public async Task NavigateAsync(AppRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        // Build first: an unknown id throws before we disturb the current
        // session, so a failed navigation leaves everything untouched.
        var next = BuildSession(route);

        // A promotion (new-session page → its detail route) adopts the very
        // same session: skip the cancel/await/dispose so an in-flight first
        // run keeps streaming.
        var previous = _current;
        var isPromotion = ReferenceEquals(previous, next);
        if (previous is not null && !isPromotion)
        {
            if (previous.State.IsRunning)
                previous.Cancel();
            await previous.WaitUntilIdleAsync();
        }

        _current = next;
        Route = route;
        RouteChanged?.Invoke(route);
        if (previous is not null && !isPromotion)
            previous.Dispose();
    }

    public void SetPendingSubmission(string text) => _pendingSubmission = text;

    public string? TakePendingSubmission()
    {
        var pending = _pendingSubmission;
        _pendingSubmission = null;
        return pending;
    }

    public IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7) =>
        new SessionManager(_env.Cwd).ListSessions(days);

    public void Dispose() => _current?.Dispose();

    private CodingSession BuildSession(AppRoute route) => route switch
    {
        ChatRoute(NewSessionRequest) => _factory.Create(FreshEnv()),
        // Promotion: navigating to the current session's own id adopts the
        // in-memory instance (a fresh session not yet persisted) instead of
        // rebuilding from disk.
        ChatRoute(ExistingSessionRequest r) when _current is not null && _current.Id == r.SessionId
            => _current,
        ChatRoute(ExistingSessionRequest r) => _factory.Resume(_env, r.SessionId),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    /// <summary>
    /// Environment for a fresh session: the startup defaults, or — when a
    /// session is already live — that session's provider/model so <c>/new</c>
    /// keeps the user connected instead of reverting to the default.
    /// </summary>
    private SessionConfig FreshEnv() =>
        _current is null ? _env : _env with
        {
            ProviderName = _current.State.ProviderName,
            Model = _current.State.Model,
        };
}
