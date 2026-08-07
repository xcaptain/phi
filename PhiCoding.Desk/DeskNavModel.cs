
namespace PhiCoding.Desk;

/// <summary>
/// Pure navigation model for the desktop shell's left pane. Free of any
/// MewUI dependency so the entry ordering and active-session highlight can
/// be unit tested without a render loop.
/// </summary>
internal static class DeskNavModel
{
    internal enum Kind
    {
        NewChat,
        Session,
        Models,
        Providers,
        Header,
    }

    internal sealed record Entry(Kind Kind, string Title, string? SessionId = null);

    /// <summary>
    /// Builds the main pane entries: a "New Chat" item, a "Sessions" header,
    /// then one item per recent session (title or the id prefix).
    /// </summary>
    internal static List<Entry> BuildMainEntries(IReadOnlyList<SessionRecord> sessions)
    {
        var entries = new List<Entry>
        {
            new(Kind.NewChat, "New Chat"),
            new(Kind.Header, "Sessions"),
        };

        foreach (var session in sessions)
        {
            var title = session.Title is { Length: > 0 } t
                ? t
                : session.Id.Length > 8 ? session.Id[..8] : session.Id;
            entries.Add(new Entry(Kind.Session, title, session.Id));
        }

        return entries;
    }

    internal static Entry[] BuildFooterEntries() =>
    [
        new(Kind.Models, "Models"),
        new(Kind.Providers, "Providers"),
    ];

    /// <summary>
    /// Returns the index of the entry for <paramref name="activeSessionId"/>,
    /// or 0 (New Chat) when the active session is unpersisted or unknown.
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
