using Phi;

namespace Phi.Avalonia.Desktop.Components;

/// <summary>
/// Pure navigation model for the desktop shell's left pane. UI-agnostic
/// (no Avalonia dependency) so the entry ordering, workspace grouping
/// and active-session highlight can be unit tested without a render
/// loop. Ported from the older shell's <c>Phi.Avalonia.NavModel</c>;
/// keeps the same semantics — the only adaptations are namespace and
/// the renaming of <c>Kind.Models / Kind.Providers</c> away (the new
/// shell has no Models sidebar entry; the Providers page is reached
/// via the bottom nav button, not a sidebar entry).
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

    /// <summary>One row's role in the nav list.</summary>
    public enum Kind
    {
        /// <summary>A persisted or in-flight chat session.</summary>
        Session,
        /// <summary>A workspace group header (sessions beneath it share
        /// one cwd).</summary>
        Workspace,
    }

    /// <summary>
    /// One nav row. <see cref="SessionId"/> is set for session rows;
    /// <see cref="Cwd"/> carries the full working directory for
    /// workspace rows (kept separate from <see cref="Title"/>, which
    /// is the display label).
    /// </summary>
    public sealed record Entry(Kind Kind, string Title, string? SessionId = null, string? Cwd = null);

    /// <summary>One workspace group: a cwd + its sessions (newest first).</summary>
    public sealed record WorkspaceGroup(string Workspace, IReadOnlyList<SessionRecord> Sessions);

    /// <summary>
    /// Builds the sessions-list entries arranged by the chosen
    /// <paramref name="mode"/>. The "New Chat" row is NOT part of this
    /// list: the shell renders it as a dedicated top button, so the
    /// list only holds workspace headers and session rows.
    /// </summary>
    public static List<Entry> BuildNavEntries(
        IReadOnlyList<SessionRecord> sessions,
        GroupMode mode)
    {
        var entries = new List<Entry>();

        if (mode == GroupMode.ByWorkspace)
        {
            foreach (var group in GroupByWorkspace(sessions))
            {
                entries.Add(new Entry(
                    Kind.Workspace,
                    WorkspaceLeafLabel(group.Workspace),
                    Cwd: group.Workspace));
                foreach (var session in group.Sessions)
                    entries.Add(new Entry(Kind.Session, TitleOf(session), SessionId: session.Id));
            }
        }
        else
        {
            foreach (var session in sessions.OrderByDescending(r => r.UpdatedAt))
                entries.Add(new Entry(Kind.Session, TitleOf(session), SessionId: session.Id));
        }

        return entries;
    }

    /// <summary>
    /// Groups sessions by their (normalized) working directory. Groups
    /// are ordered by the newest session's
    /// <see cref="SessionRecord.UpdatedAt"/> descending; sessions
    /// within a group are newest first.
    /// </summary>
    public static IReadOnlyList<WorkspaceGroup> GroupByWorkspace(IReadOnlyList<SessionRecord> sessions)
    {
        return [.. sessions
            .GroupBy(r => Path.GetFullPath(r.Cwd), StringComparer.OrdinalIgnoreCase)
            .Select(g => new WorkspaceGroup(
                g.First().Cwd,
                [.. g.OrderByDescending(r => r.UpdatedAt)]))
            .OrderByDescending(g => g.Sessions[0].UpdatedAt)];
    }

    /// <summary>Shortens a workspace path for display: home → <c>~</c>.</summary>
    public static string WorkspaceLabel(string cwd)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return cwd;
        return cwd.StartsWith(home, StringComparison.Ordinal) ? "~" + cwd[home.Length..] : cwd;
    }

    /// <summary>
    /// Last path segment of a workspace, for compact sidebar display
    /// (e.g. <c>/Users/me/github/phi</c> → <c>phi</c>). Handles both
    /// slash orientations so records created on any OS render
    /// consistently. Falls back to the full label when there's no
    /// separator. The backend session record keeps the full cwd — this
    /// is display-only.
    /// </summary>
    public static string WorkspaceLeafLabel(string cwd)
    {
        var trimmed = cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sepIndex = trimmed.LastIndexOfAny(PathSeparators);
        return sepIndex >= 0 && sepIndex < trimmed.Length - 1
            ? trimmed[(sepIndex + 1)..]
            : WorkspaceLabel(cwd);
    }

    private static readonly char[] PathSeparators = ['/', '\\'];

    /// <summary>
    /// Display label for a session row. Falls back to the first 8 chars
    /// of the session id when the LLM auto-namer hasn't produced a
    /// title yet.
    /// </summary>
    public static string TitleOf(SessionRecord session) =>
        !string.IsNullOrEmpty(session.Title)
            ? session.Title
            : session.Id.Length > 8 ? session.Id[..8] : session.Id;

    /// <summary>
    /// Returns the index of the entry for
    /// <paramref name="activeSessionId"/>, or -1 when the active
    /// session is unpersisted or unknown (nothing to highlight). Works
    /// across grouped entries (session rows live under workspace
    /// headers, or flat in ByDate mode).
    /// </summary>
    public static int IndexForActive(IReadOnlyList<Entry> entries, string? activeSessionId)
    {
        if (string.IsNullOrEmpty(activeSessionId)) return -1;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Kind == Kind.Session && entries[i].SessionId == activeSessionId)
                return i;
        }
        return -1;
    }
}
