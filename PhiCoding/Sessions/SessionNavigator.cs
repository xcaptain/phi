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
            : _factory.Resume(EnvFor(resumeSessionId), resumeSessionId);
    }

    public ISession Current => _current;

    public event Action? SessionChanged;

    public async Task NavigateToNewAsync(string? cwd = null)
    {
        var next = _factory.Create(FreshEnv(cwd));
        await SwapAsync(next);
    }

    public async Task ResumeAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new InvalidOperationException("Cannot resume an empty session id.");
        // Resolve the session's own working directory (it may live in a
        // different workspace than the navigator's configured cwd) so
        // cross-workspace resume works. The config only supplies the
        // environment; the record's provider/model still win.
        var next = _factory.Resume(EnvFor(sessionId), sessionId);
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
    /// reverting to the default. <paramref name="cwd"/> overrides the working
    /// directory (used by the desktop to start a chat in a chosen workspace).
    /// </summary>
    private SessionConfig FreshEnv(string? cwd = null) => _env with
    {
        Cwd = cwd ?? _env.Cwd,
        ProviderName = _current.State.ProviderName,
        Model = _current.State.Model,
    };

    /// <summary>
    /// Environment for resuming <paramref name="sessionId"/>: the navigator's
    /// config with <see cref="SessionConfig.Cwd"/> set to the session record's
    /// own working directory so cross-workspace resume reads the right
    /// transcript. Falls back to the configured cwd (and lets
    /// <see cref="CodingSessionFactory.Resume"/> throw) when the id is
    /// unknown.
    /// </summary>
    private SessionConfig EnvFor(string sessionId) =>
        WorkspaceSessionStore.FindSession(sessionId) is { } record
            ? _env with { Cwd = record.Cwd }
            : _env;
}
