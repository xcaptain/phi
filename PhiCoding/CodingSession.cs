using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Application-level session wrapper: ties a <see cref="SessionStorage"/>
/// (the conversation JSONL) and a <see cref="SessionIndex"/> entry
/// (metadata in <c>index.jsonl</c>) together, plus the conversion between
/// <see cref="IAgentMessage"/> and <see cref="SessionEntry"/>.
/// Sessions are scoped to a project (cwd) — see <see cref="SessionPaths"/>.
/// </summary>
public sealed class CodingSession
{
    private readonly SessionIndex _index;
    private readonly object _lock = new();
    private string _cwd;

    private CodingSession(
        SessionRecord record,
        SessionStorage storage,
        SessionIndex index,
        string cwd)
    {
        Record = record;
        Storage = storage;
        _index = index;
        _cwd = cwd;
    }

    public string Id => Record.Id;
    public string Cwd => _cwd;
    public string Model => Record.Model;
    public SessionRecord Record { get; private set; }
    public SessionStorage Storage { get; }

    /// <summary>
    /// Returns the default session for <paramref name="cwd"/>: a stable
    /// per-project id (format <c>default-{sha256(projectKey)[:8]}</c>) that
    /// is created on first call and reused on every subsequent call.
    /// </summary>
    public static CodingSession GetOrCreateDefault(string cwd, string model)
    {
        SessionPaths.EnsureRootFor(cwd);
        var index = new SessionIndex(SessionPaths.IndexFileFor(cwd));
        var id = SessionPaths.DefaultSessionId(cwd);
        var existing = index.Get(id);
        if (existing is not null)
            return new CodingSession(existing, OpenStorage(cwd, id), index, cwd);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var record = new SessionRecord(id, cwd, model, "Default session", now, now);
        index.Upsert(record);
        return new CodingSession(record, OpenStorage(cwd, id), index, cwd);
    }

    /// <summary>
    /// Creates a brand-new session with a random id and registers it in
    /// the per-project index. Use <see cref="Resume"/> to open an existing one.
    /// </summary>
    public static CodingSession Create(string cwd, string model, string? title = null)
    {
        SessionPaths.EnsureRootFor(cwd);
        var index = new SessionIndex(SessionPaths.IndexFileFor(cwd));
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var record = new SessionRecord(id, cwd, model, title, now, now);
        index.Upsert(record);
        return new CodingSession(record, OpenStorage(cwd, id), index, cwd);
    }

    /// <summary>
    /// Resumes a previously-created session by id. Throws if the session
    /// does not exist in the per-project index.
    /// </summary>
    public static CodingSession Resume(string id, string cwd)
    {
        SessionPaths.EnsureRootFor(cwd);
        var indexPath = SessionPaths.IndexFileFor(cwd);
        var index = new SessionIndex(indexPath);
        var record = index.Get(id) ?? throw new InvalidOperationException(
            $"Session '{id}' not found for project '{cwd}'");
        return new CodingSession(record, OpenStorage(cwd, id), index, cwd);
    }

    public void AppendMessage(IAgentMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var entry = SessionEntryConverter.FromAgentMessage(message);
        lock (_lock) Storage.Append(entry);
        Touch();
    }

    public IReadOnlyList<IAgentMessage> LoadMessages()
    {
        lock (_lock)
        {
            return Storage.ReadAll()
                .Select(SessionEntryConverter.ToAgentMessage)
                .ToList();
        }
    }

    /// <summary>
    /// Updates <c>UpdatedAt</c> in the index record.
    /// </summary>
    public void Touch()
    {
        var touched = Record with
        {
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        _index.Upsert(touched);
        Record = touched;
    }

    public void Rename(string? newTitle)
    {
        var renamed = Record with { Title = newTitle };
        _index.Upsert(renamed);
        Record = renamed;
    }

    private static SessionStorage OpenStorage(string cwd, string id) =>
        new(SessionPaths.SessionFileFor(cwd, id));
}
