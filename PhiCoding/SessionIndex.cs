using System.Text.Json;

namespace PhiCoding;

/// <summary>
/// Global session index, stored as JSONL at
/// <see cref="SessionPaths.IndexFile"/>. One <see cref="SessionRecord"/>
/// per line. Lookups and listings are in-memory after the file is loaded;
/// writes append-and-replace the whole file (small N, simple invariant).
/// Thread-safe via an internal lock so a session's <c>Touch</c> call
/// triggered from the UI thread doesn't race a programmatic <c>List</c>.
/// </summary>
public sealed class SessionIndex(string indexPath)
{
    private readonly object _lock = new();

    public IReadOnlyList<SessionRecord> ListAll()
    {
        var all = ReadRecords();
        return all.OrderByDescending(r => r.UpdatedAt).ToList();
    }

    public IReadOnlyList<SessionRecord> ListForCwd(string cwd)
    {
        var resolved = Path.GetFullPath(cwd);
        return ListAll().Where(r => Path.GetFullPath(r.Cwd) == resolved).ToList();
    }

    public SessionRecord? Get(string id)
    {
        return ReadRecords().FirstOrDefault(r => r.Id == id);
    }

    public void Upsert(SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var parent = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        lock (_lock)
        {
            var records = ReadRecordsUnsafe();
            var existing = records.FindIndex(r => r.Id == record.Id);
            if (existing >= 0) records[existing] = record;
            else records.Add(record);

            var lines = records.Select(r => JsonSerializer.Serialize(r, PhiJsonContext.Default.SessionRecord)).ToList();
            var content = string.Join("\n", lines) + "\n";
            File.WriteAllText(indexPath, content);
        }
    }

    private List<SessionRecord> ReadRecords()
    {
        lock (_lock) return ReadRecordsUnsafe();
    }

    private List<SessionRecord> ReadRecordsUnsafe()
    {
        if (!File.Exists(indexPath)) return [];
        var records = new List<SessionRecord>();
        foreach (var line in File.ReadAllLines(indexPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            records.Add(JsonSerializer.Deserialize(line, PhiJsonContext.Default.SessionRecord)
                ?? throw new InvalidDataException(
                    $"Failed to parse session index entry: {line}"));
        }
        return records;
    }
}
