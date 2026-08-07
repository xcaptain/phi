namespace PhiCoding.Desk;

/// <summary>
/// Pure navigation model for the desktop shell's left pane. Free of any
/// MewUI dependency so the entry ordering, workspace grouping and
/// active-session highlight can be unit tested without a render loop.
/// </summary>
internal static class DeskNavModel
{
    /// <summary>How the sessions list is organised.</summary>
    internal enum GroupMode
    {
        /// <summary>All sessions in one flat list, newest first.</summary>
        ByDate,
        /// <summary>Sessions grouped by working directory.</summary>
        ByWorkspace,
    }

    /// <summary>
    /// Whether the navigation pane is collapsed (icon-only) or expanded
    /// (icon + text). Mirrors the relevant subset of MewUI's
    /// <c>PaneDisplayMode</c>; we ignore <c>Minimal</c>/<c>Auto</c> so the
    /// shell stays a simple Expanded ↔ Compact toggle.
    /// </summary>
    internal enum PaneMode
    {
        Expanded,
        Compact,
    }

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
        /// <summary>The "By date ▾ / By workspace ▾" toggle row in the pane.</summary>
        ToggleRow,
    }

    internal sealed record Entry(
        Kind Kind,
        string Title,
        string? SessionId = null,
        GroupMode ToggleMode = default);

    /// <summary>One workspace group: a cwd + its sessions (newest first).</summary>
    internal sealed record WorkspaceGroup(string Workspace, IReadOnlyList<SessionRecord> Sessions);

    /// <summary>
    /// Builds the main pane entries. In <see cref="PaneMode.Compact"/> only
    /// "New Chat" is included (the footer is owned by the caller and the
    /// sessions region is hidden entirely). In <see cref="PaneMode.Expanded"/>
    /// a toggle row precedes the sessions, and the sessions are arranged by
    /// the chosen <paramref name="mode"/>.
    /// </summary>
    internal static List<Entry> BuildMainEntries(
        IReadOnlyList<SessionRecord> sessions,
        GroupMode mode,
        PaneMode paneMode)
    {
        var entries = new List<Entry> { new(Kind.NewChat, "New Chat") };

        // Compact: only "New Chat". Footer is the caller's responsibility.
        if (paneMode == PaneMode.Compact)
            return entries;

        // Expanded: the toggle row sits in the "Sessions" header slot.
        // The shell renders the actual toggle button via a per-entry content
        // selector — the model only flags which mode the toggle should show.
        entries.Add(new Entry(Kind.ToggleRow, ToggleLabel(mode), ToggleMode: mode));

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

    /// <summary>Display label for the sessions-area toggle row.</summary>
    internal static string ToggleLabel(GroupMode mode) => mode switch
    {
        GroupMode.ByDate => "By date ▾",
        GroupMode.ByWorkspace => "By workspace ▾",
        _ => "By workspace ▾",
    };

    /// <summary>
    /// Display label for a session row. Falls back to the first 8 chars of
    /// the session id when the LLM auto-namer hasn't produced a title yet.
    /// </summary>
    internal static string TitleOf(SessionRecord session) =>
        session.Title is { Length: > 0 } t
            ? t
            : session.Id.Length > 8 ? session.Id[..8] : session.Id;

    internal static Entry[] BuildFooterEntries() =>
    [
        new(Kind.Models, "Models"),
        new(Kind.Providers, "Providers"),
    ];

    /// <summary>
    /// Returns the index of the entry for <paramref name="activeSessionId"/>,
    /// or 0 (New Chat) when the active session is unpersisted or unknown.
    /// Works across grouped entries (session rows live under workspace
    /// headers, or flat in ByDate mode).
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