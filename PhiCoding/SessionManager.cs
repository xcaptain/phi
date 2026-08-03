namespace PhiCoding;

/// <summary>
/// Owns the per-project session index (<see cref="SessionIndex"/>) and
/// decides <em>when</em> a session is persisted. Ported from tau's
/// <c>tau_coding.session_manager.SessionManager</c>.
/// <para>
/// The key distinction is <see cref="PrepareSession"/> vs
/// <see cref="CreateSession"/>: prepare allocates an id and a record
/// <b>without touching disk</b> (used for fresh TUI sessions that may
/// never receive a message); create upserts the index immediately.
/// A prepared session becomes visible to <see cref="ListSessions"/> only
/// after the first <see cref="Upsert"/>.
/// </para>
/// </summary>
public sealed class SessionManager(string cwd)
{
    private readonly SessionIndex _index = new SessionIndex(SessionPaths.IndexFileFor(cwd));

    /// <summary>Project working directory this manager serves.</summary>
    public string Cwd { get; } = cwd;

    /// <summary>
    /// Allocates a session record without writing anything to disk —
    /// no index entry, no session file, not even the project directory.
    /// The record becomes indexed lazily via <see cref="Upsert"/>.
    /// </summary>
    public SessionRecord PrepareSession(string model, string? title = null, string providerName = "")
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new SessionRecord(
            Guid.NewGuid().ToString("N"), Cwd, model, title, now, now, providerName);
    }

    /// <summary>
    /// Allocates a session record and indexes it immediately.
    /// </summary>
    public SessionRecord CreateSession(string model, string? title = null, string providerName = "")
    {
        var record = PrepareSession(model, title, providerName);
        Upsert(record);
        return record;
    }

    /// <summary>
    /// Returns the project's stable default session, creating and indexing
    /// it on first use.
    /// </summary>
    public SessionRecord GetOrCreateDefaultSession(string model, string providerName = "")
    {
        var existing = FindSession(SessionPaths.DefaultSessionId(Cwd));
        if (existing is not null) return existing;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var record = new SessionRecord(
            SessionPaths.DefaultSessionId(Cwd), Cwd, model,
            "Default session", now, now, providerName);
        Upsert(record);
        return record;
    }

    /// <summary>Returns the indexed record or throws when unknown.</summary>
    public SessionRecord GetSession(string id) =>
        FindSession(id) ?? throw new InvalidOperationException(
            $"Session '{id}' not found for project '{Cwd}'");

    /// <summary>Returns the indexed record, or null when unknown.</summary>
    public SessionRecord? FindSession(string id) => _index.Get(id);

    /// <summary>
    /// Indexed sessions last touched within <paramref name="days"/> days,
    /// newest first.
    /// </summary>
    public IReadOnlyList<SessionRecord> ListSessions(int days = 7)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds();
        return [.. _index.ListAll().Where(r => r.UpdatedAt >= cutoff)];
    }

    /// <summary>Inserts or replaces a record in the index.</summary>
    public void Upsert(SessionRecord record) => _index.Upsert(record);

    /// <summary>JSONL transcript path for a session of this project.</summary>
    public string SessionFileFor(string sessionId) =>
        SessionPaths.SessionFileFor(Cwd, sessionId);
}
