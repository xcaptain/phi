namespace PhiCoding.Sessions;

/// <summary>
/// Default <see cref="ISessionNavigator"/>: builds sessions through a
/// <see cref="CodingSessionFactory"/>, owns the current session's lifecycle,
/// and publishes session changes via <see cref="SessionChanged"/>.
/// <para>
/// A fresh session carries over the current session's provider + model when
/// one exists, so <c>/new</c> keeps the user connected; on first create it
/// uses the environment config's defaults.
/// </para>
/// </summary>
public sealed class SessionNavigator : ISessionNavigator
{
    private readonly CodingSessionFactory _factory;
    private readonly SessionConfig _env;
    private CodingSession _current;

    public SessionNavigator(
        CodingSessionFactory factory, SessionConfig env, string? resumeSessionId)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(env);
        _factory = factory;
        _env = env;
        // Synchronous build at startup so an unknown id surfaces before the
        // TUI mounts (no session is in flight at this point to settle).
        _current = resumeSessionId is null
            ? _factory.Create(_env)
            : _factory.Resume(_env, resumeSessionId);
    }

    public ISession Current => _current;

    public event Action? SessionChanged;

    public async Task NavigateToNewAsync()
    {
        var next = _factory.Create(FreshEnv());
        await SwapAsync(next);
    }

    public async Task ResumeAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new InvalidOperationException("Cannot resume an empty session id.");
        var next = _factory.Resume(_env, sessionId);
        await SwapAsync(next);
    }

    public IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7) =>
        new SessionManager(_env.Cwd).ListSessions(days);

    public void Dispose() => _current.Dispose();

    private async Task SwapAsync(CodingSession next)
    {
        var previous = _current;
        if (previous.State.IsRunning)
            previous.Cancel();
        await previous.WaitUntilIdleAsync();

        _current = next;
        SessionChanged?.Invoke();
        previous.Dispose();
    }

    /// <summary>
    /// Environment for a fresh session: the startup defaults on first
    /// create, or — when a session is already live — that session's
    /// provider/model so <c>/new</c> keeps the user connected instead of
    /// reverting to the default.
    /// </summary>
    private SessionConfig FreshEnv() => _env with
    {
        ProviderName = _current.State.ProviderName,
        Model = _current.State.Model,
    };
}
