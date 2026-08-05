namespace PhiCoding.Sessions;

/// <summary>
/// Default <see cref="ISessionNavigator"/>: builds sessions through a
/// <see cref="CodingSessionFactory"/>, owns the current session's lifecycle,
/// and publishes route changes via <see cref="RouteChanged"/>.
/// <para>
/// A fresh session (<see cref="NewSessionRoute"/>) carries over the current
/// session's provider + model when one exists, so <c>/new</c> keeps the user
/// connected; at startup it uses the environment config's defaults.
/// </para>
/// </summary>
public sealed class SessionNavigator : ISessionNavigator
{
    private readonly CodingSessionFactory _factory;
    private readonly SessionConfig _env;
    private CodingSession? _current;

    public SessionNavigator(
        CodingSessionFactory factory, SessionConfig env, SessionRoute initialRoute)
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

    public SessionRoute Route { get; private set; }

    public event Action<SessionRoute>? RouteChanged;

    public async Task NavigateAsync(SessionRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        // Build first: an unknown id throws before we disturb the current
        // session, so a failed navigation leaves everything untouched.
        var next = BuildSession(route);

        var previous = _current;
        if (previous is not null)
        {
            if (previous.State.IsRunning)
                previous.Cancel();
            await previous.WaitUntilIdleAsync();
        }

        _current = next;
        Route = route;
        RouteChanged?.Invoke(route);
        previous?.Dispose();
    }

    public IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7) =>
        new SessionManager(_env.Cwd).ListSessions(days);

    public void Dispose() => _current?.Dispose();

    private CodingSession BuildSession(SessionRoute route) => route switch
    {
        NewSessionRoute => _factory.Create(FreshEnv()),
        ExistingSessionRoute r => _factory.Resume(_env, r.SessionId),
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
