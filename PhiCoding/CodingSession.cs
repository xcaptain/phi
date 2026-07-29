using System.Security.Cryptography;
using System.Text;
using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Application-level session wrapper: ties a <see cref="SessionStorage"/>
/// (the conversation JSONL) and a <see cref="SessionIndex"/> entry
/// (metadata in <c>index.jsonl</c>) together, plus the conversion between
/// <see cref="IAgentMessage"/> and <see cref="SessionEntry"/>. This is the
/// type callers use to append / load / create / resume sessions.
/// </summary>
public sealed class CodingSession
{
    private readonly SessionIndex _index;
    private readonly object _lock = new();

    private CodingSession(
        SessionRecord record,
        SessionStorage storage,
        SessionIndex index)
    {
        Record = record;
        Storage = storage;
        _index = index;
    }

    public string Id => Record.Id;
    public string Cwd => Record.Cwd;
    public string Model => Record.Model;
    public SessionRecord Record { get; private set; }
    public SessionStorage Storage { get; }

    /// <summary>
    /// Returns the default session for <paramref name="cwd"/> under the
    /// given <paramref name="root"/>: a stable per-cwd session id
    /// (<c>default-{sha256(cwd)[:8]}</c>) that is created on first call and
    /// reused on every subsequent call. This is the "yesterday's work
    /// continues today" entry point.
    /// </summary>
    public static CodingSession GetOrCreateDefault(string cwd, string model, string root)
    {
        SessionPaths.EnsureRoot(root);
        var index = new SessionIndex(SessionPaths.IndexFileIn(root));
        var id = DefaultSessionId(cwd);
        var existing = index.Get(id);
        if (existing is not null) return new CodingSession(existing, OpenStorage(root, id), index);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var record = new SessionRecord(id, cwd, model, "Default session", now, now);
        index.Upsert(record);
        return new CodingSession(record, OpenStorage(root, id), index);
    }

    /// <summary>
    /// Creates a brand-new session with a generated id under the given
    /// <paramref name="root"/>. Use <see cref="Resume(string,string)"/> to
    /// open an existing one.
    /// </summary>
    public static CodingSession Create(string cwd, string model, string root, string? title = null)
    {
        SessionPaths.EnsureRoot(root);
        var index = new SessionIndex(SessionPaths.IndexFileIn(root));
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var record = new SessionRecord(id, cwd, model, title, now, now);
        index.Upsert(record);
        return new CodingSession(record, OpenStorage(root, id), index);
    }

    /// <summary>
    /// Resumes a previously-created session by id under the given
    /// <paramref name="root"/>. Throws if the session does not exist in
    /// the index.
    /// </summary>
    public static CodingSession Resume(string id, string root)
    {
        SessionPaths.EnsureRoot(root);
        var indexPath = SessionPaths.IndexFileIn(root);
        var index = new SessionIndex(indexPath);
        var record = index.Get(id);
        if (record is null)
        {
            // Surface the actual file contents to make the failure mode
            // obvious in CI logs — a missing session is almost always a
            // race (parallel test, wrong root) or a corrupted index.
            var indexContents = File.Exists(indexPath)
                ? File.ReadAllText(indexPath)
                : "<missing>";
            throw new InvalidOperationException(
                $"Session '{id}' not found in {indexPath}. Index contents:\n{indexContents}");
        }
        return new CodingSession(record, OpenStorage(root, id), index);
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
    /// Updates <c>UpdatedAt</c> in the index record. Call after any change
    /// that should bump the session's recency (message append, rename, etc.).
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

    /// <summary>
    /// Stable id for the per-cwd default session. SHA-256 over the resolved
    /// cwd, truncated to 8 hex chars — short enough to be readable, long
    /// enough that collision across unrelated projects is astronomically
    /// unlikely.
    /// </summary>
    public static string DefaultSessionId(string cwd)
    {
        var resolved = Path.GetFullPath(cwd);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resolved));
        return "default-" + Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private static SessionStorage OpenStorage(string root, string id) =>
        new(SessionPaths.SessionFileIn(root, id));
}
