namespace PhiAgent;

/// <summary>
/// Append-only JSONL storage for one <see cref="SessionEntry"/> stream.
/// One file per session; entries are written in append order, never
/// reordered or partially rewritten (except via <see cref="Clear"/>, which
/// removes the whole file). Reads tolerate missing files (empty session)
/// and tolerate blank lines between entries (forward-compat with external
/// tooling that injects separators).
/// <para>
/// Concurrency model: a single instance is thread-safe via an internal lock
/// so the runtime can write from multiple continuations (e.g. a background
/// compaction task plus a foreground user prompt) without serializing at
/// the call site. Different instances pointing at the same path are <b>not</b>
/// safe — the file is the lock.
/// </para>
/// </summary>
public sealed class SessionStorage(string path)
{
    private readonly string _path = path ?? throw new ArgumentNullException(nameof(path));
    private readonly Lock _lock = new();

    public string FilePath => _path;

    public void Append(SessionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var line = SessionEntryCodec.Serialize(entry);
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, line);
        }
    }

    public IReadOnlyList<SessionEntry> ReadAll()
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return [];
            var lines = File.ReadAllLines(_path);
            var entries = new List<SessionEntry>(lines.Length);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    entries.Add(SessionEntryCodec.Deserialize(line));
                }
                catch (InvalidDataException ex)
                {
                    throw new InvalidDataException(
                        $"Failed to parse session entry on line {i + 1} of {_path}: {ex.Message}",
                        ex);
                }
            }
            return entries;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
