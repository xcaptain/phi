namespace PhiCoding.Desk;

/// <summary>
/// Pure navigation model for the desktop shell's left pane. Free of any
/// MewUI dependency so the entry ordering, workspace grouping and
/// active-session highlight can be unit tested without a render loop.
/// </summary>
internal static class DeskNavModel
{
    internal enum Kind
    {
        NewChat,
        Session,
        Models,
        Providers,
        /// <summary>Section header (e.g. "Sessions").</summary>
        Header,
        /// <summary>Workspace group header; sessions beneath it share one cwd.</summary>
        Workspace,
    }

    internal sealed record Entry(Kind Kind, string Title, string? SessionId = null);

    /// <summary>One workspace group: a cwd + its sessions (newest first).</summary>
    internal sealed record WorkspaceGroup(string Workspace, IReadOnlyList<SessionRecord> Sessions);

    /// <summary>
    /// Builds the main pane entries: "New Chat", a "Sessions" header, then
    /// sessions grouped by working directory (a <see cref="Kind.Workspace"/>
    /// header per group). Workspaces are ordered by their newest session,
    /// sessions by date within each group.
    /// </summary>
    internal static List<Entry> BuildMainEntries(IReadOnlyList<SessionRecord> sessions)
    {
        var entries = new List<Entry>
        {
            new(Kind.NewChat, "New Chat"),
            new(Kind.Header, "Sessions"),
        };

        foreach (var group in GroupByWorkspace(sessions))
        {
            entries.Add(new Entry(Kind.Workspace, WorkspaceLabel(group.Workspace)));
            foreach (var session in group.Sessions)
            {
                var title = session.Title is { Length: > 0 } t
                    ? t
                    : session.Id.Length > 8 ? session.Id[..8] : session.Id;
                entries.Add(new Entry(Kind.Session, title, session.Id));
            }
        }

        return entries;
    }

    /// <summary>
    /// Groups sessions by their (normalized) working directory. Groups are
    /// ordered by the newest session's <see cref="SessionRecord.UpdatedAt"/>
    /// descending; sessions within a group are newest first.
    /// </summary>
    internal static IReadOnlyList<WorkspaceGroup> GroupByWorkspace(IReadOnlyList<SessionRecord> sessions)
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
    internal static string WorkspaceLabel(string cwd)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return cwd.StartsWith(home, StringComparison.Ordinal) ? "~" + cwd[home.Length..] : cwd;
    }

    internal static Entry[] BuildFooterEntries() =>
    [
        new(Kind.Models, "Models"),
        new(Kind.Providers, "Providers"),
    ];

    /// <summary>
    /// Returns the index of the entry for <paramref name="activeSessionId"/>,
    /// or 0 (New Chat) when the active session is unpersisted or unknown.
    /// Works across grouped entries (session rows live under workspace
    /// headers).
    /// </summary>
    internal static int IndexForActive(IReadOnlyList<Entry> entries, string? activeSessionId)
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
