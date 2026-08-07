namespace PhiCoding;

/// <summary>
/// Global view over every project's session index under
/// <see cref="SessionPaths.DefaultRoot"/>. Each cwd owns its own
/// <c>index.jsonl</c>; this store scans them all and merges the records so a
/// frontend that is not bound to a single working directory (the desktop
/// app) can list sessions across every workspace, find a session by id
/// regardless of which project it lives in, and enumerate the distinct
/// working directories.
/// </summary>
public static class WorkspaceSessionStore
{
    /// <summary>
    /// Every indexed session across all workspaces, filtered to those touched
    /// within <paramref name="days"/> days and ordered newest first.
    /// </summary>
    public static IReadOnlyList<SessionRecord> ListAllSessions(int days = 7)
    {
        if (!Directory.Exists(SessionPaths.DefaultRoot)) return [];

        // days <= 0 means "no cutoff" (used by ListWorkspaces / FindSession).
        var cutoff = days > 0
            ? DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds()
            : 0L;
        var records = new List<SessionRecord>();
        foreach (var dir in Directory.EnumerateDirectories(SessionPaths.DefaultRoot))
        {
            var indexPath = Path.Combine(dir, SessionPaths.IndexFileName);
            if (!File.Exists(indexPath)) continue;
            records.AddRange(new SessionIndex(indexPath).ListAll());
        }

        return [.. records
            .Where(r => days <= 0 || r.UpdatedAt >= cutoff)
            .OrderByDescending(r => r.UpdatedAt)];
    }

    /// <summary>
    /// Distinct working directories that have indexed sessions, ordered by
    /// their newest session's activity (most recent first). The workspace
    /// label/selection is derived from these records — nothing is persisted.
    /// </summary>
    public static IReadOnlyList<string> ListWorkspaces()
    {
        return [.. ListAllSessions(0)
            .GroupBy(r => Path.GetFullPath(r.Cwd), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(r => r.UpdatedAt))
            .Select(g => g.First().Cwd)];
    }

    /// <summary>Finds a session by id across every workspace, or null.</summary>
    public static SessionRecord? FindSession(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return ListAllSessions(0).FirstOrDefault(r => r.Id == id);
    }

    /// <summary>Finds a session by id across every workspace, or throws.</summary>
    public static SessionRecord GetSession(string id) =>
        FindSession(id) ?? throw new InvalidOperationException($"Session '{id}' not found");
}
