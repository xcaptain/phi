namespace PhiCoding.Avalonia;

/// <summary>
/// Pure navigation model for the desktop shell's left pane. Free of any
/// Avalonia dependency so the entry ordering, workspace grouping and
/// active-session highlight can be unit tested without a render loop.
/// Ported from the MewUI shell's <c>DeskNavModel</c>.
/// </summary>
public static class NavModel
{
    /// <summary>How the sessions list is organised.</summary>
    public enum GroupMode
    {
        /// <summary>All sessions in one flat list, newest first.</summary>
        ByDate,
        /// <summary>Sessions grouped by working directory.</summary>
        ByWorkspace,
    }

    public enum Kind
    {
        NewChat,
        Session,
        Models,
        Providers,
        /// <summary>Workspace group header; sessions beneath it share one cwd.</summary>
        Workspace,
    }

    public sealed record Entry(Kind Kind, string Title, string? SessionId = null);

    /// <summary>One workspace group: a cwd + its sessions (newest first).</summary>
    public sealed record WorkspaceGroup(string Workspace, IReadOnlyList<SessionRecord> Sessions);

    /// <summary>
    /// Builds the main pane entries: a "New Chat" row followed by the
    /// sessions arranged by the chosen <paramref name="mode"/>.
    /// </summary>
    public static List<Entry> BuildMainEntries(
        IReadOnlyList<SessionRecord> sessions,
        GroupMode mode)
    {
        var entries = new List<Entry> { new(Kind.NewChat, "New Chat") };

        if (mode == GroupMode.ByWorkspace)
        {
            foreach (var group in GroupByWorkspace(sessions))
            {
                entries.Add(new Entry(Kind.Workspace, WorkspaceLabel(group.Workspace)));
                foreach (var session in group.Sessions)
                    entries.Add(new Entry(Kind.Session, TitleOf(session), session.Id));
            }
        }
        else
        {
            foreach (var session in sessions.OrderByDescending(r => r.UpdatedAt))
                entries.Add(new Entry(Kind.Session, TitleOf(session), session.Id));
        }

        return entries;
    }

    /// <summary>
    /// Groups sessions by their (normalized) working directory. Groups are
    /// ordered by the newest session's <see cref="SessionRecord.UpdatedAt"/>
    /// descending; sessions within a group are newest first.
    /// </summary>
    public static IReadOnlyList<WorkspaceGroup> GroupByWorkspace(IReadOnlyList<SessionRecord> sessions)
    {
        return sessions
            .GroupBy(r => Path.GetFullPath(r.Cwd), StringComparer.OrdinalIgnoreCase)
            .Select(g => new WorkspaceGroup(
                g.First().Cwd,
                g.OrderByDescending(r => r.UpdatedAt).ToList()))
            .OrderByDescending(g => g.Sessions[0].UpdatedAt)
            .ToList();
    }

    /// <summary>Shortens a workspace path for display: home → <c>~</c>.</summary>
    public static string WorkspaceLabel(string cwd)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return cwd.StartsWith(home, StringComparison.Ordinal) ? "~" + cwd[home.Length..] : cwd;
    }

    /// <summary>
    /// Display label for a session row. Falls back to the first 8 chars of
    /// the session id when the LLM auto-namer hasn't produced a title yet.
    /// </summary>
    public static string TitleOf(SessionRecord session) =>
        session.Title is { Length: > 0 } t
            ? t
            : session.Id.Length > 8 ? session.Id[..8] : session.Id;

    /// <summary>
    /// Returns the index of the entry for <paramref name="activeSessionId"/>,
    /// or 0 (New Chat) when the active session is unpersisted or unknown.
    /// Works across grouped entries (session rows live under workspace
    /// headers, or flat in ByDate mode).
    /// </summary>
    public static int IndexForActive(IReadOnlyList<Entry> entries, string? activeSessionId)
    {
        if (activeSessionId is { Length: > 0 })
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Kind == Kind.Session && entries[i].SessionId == activeSessionId)
                    return i;
            }
        }
        return 0;
    }
}
