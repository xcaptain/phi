using PhiCoding.Sessions;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="NavModel"/>: pure entry-building, workspace grouping, and
/// active-session highlight. Ported from the MewUI desk's DeskNavModel
/// tests so the ordering contract is preserved across the migration.
/// </summary>
[NotInParallel("Avalonia-Nav")]
public class NavModelTests
{
    private static SessionRecord Record(string id, string cwd, long updatedAt, string? title = null) => new(
        id, cwd, "m", title, updatedAt, updatedAt);

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Test]
    public async Task BuildMainEntries_NewChatFirst_ByDate()
    {
        var sessions = new[]
        {
            Record("a", "/w1", NowMs(), "first"),
            Record("b", "/w1", NowMs() - 100, "second"),
        };

        var entries = NavModel.BuildMainEntries(sessions, NavModel.GroupMode.ByDate);

        await Assert.That(entries[0].Kind).IsEqualTo(NavModel.Kind.NewChat);
        // Newest first within the flat list.
        await Assert.That(entries[1].Kind).IsEqualTo(NavModel.Kind.Session);
        await Assert.That(entries[1].SessionId).IsEqualTo("a");
        await Assert.That(entries[2].SessionId).IsEqualTo("b");
    }

    [Test]
    public async Task BuildMainEntries_ByWorkspace_GroupsUnderHeaders()
    {
        var sessions = new[]
        {
            Record("a", "/w1", NowMs()),
            Record("b", "/w2", NowMs() - 50),
            Record("c", "/w1", NowMs() - 100),
        };

        var entries = NavModel.BuildMainEntries(sessions, NavModel.GroupMode.ByWorkspace);

        // NewChat, /w1 header, a, c, /w2 header, b
        await Assert.That(entries.Count).IsEqualTo(6);
        await Assert.That(entries[1].Kind).IsEqualTo(NavModel.Kind.Workspace);
        await Assert.That(entries[1].Title).IsEqualTo(NavModel.WorkspaceLabel(Path.GetFullPath("/w1")));
        await Assert.That(entries[2].SessionId).IsEqualTo("a");
        await Assert.That(entries[3].SessionId).IsEqualTo("c");
        await Assert.That(entries[4].Kind).IsEqualTo(NavModel.Kind.Workspace);
        await Assert.That(entries[5].SessionId).IsEqualTo("b");
    }

    [Test]
    public async Task WorkspaceGroups_OrderedByNewestSession()
    {
        var sessions = new[]
        {
            Record("old", "/w1", NowMs() - 1000),
            Record("new", "/w2", NowMs()),
        };

        var groups = NavModel.GroupByWorkspace(sessions);

        // /w2 has the newest session, so it comes first.
        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(groups[0].Workspace).IsEqualTo(Path.GetFullPath("/w2"));
        await Assert.That(groups[1].Workspace).IsEqualTo(Path.GetFullPath("/w1"));
    }

    [Test]
    public async Task TitleOf_FallsBackToIdPrefix()
    {
        await Assert.That(NavModel.TitleOf(Record("abcdefghij", "/w1", NowMs(), "Hello"))).IsEqualTo("Hello");
        await Assert.That(NavModel.TitleOf(Record("abcdefghij", "/w1", NowMs()))).IsEqualTo("abcdefgh");
    }

    [Test]
    public async Task IndexForActive_UnknownSession_ReturnsNewChatIndex()
    {
        var entries = NavModel.BuildMainEntries(
            [Record("a", "/w1", NowMs(), "A")],
            NavModel.GroupMode.ByDate);

        await Assert.That(NavModel.IndexForActive(entries, "missing")).IsEqualTo(0);
        await Assert.That(NavModel.IndexForActive(entries, null)).IsEqualTo(0);
    }

    [Test]
    public async Task IndexForActive_FindsSessionAcrossGroups()
    {
        var sessions = new[]
        {
            Record("a", "/w1", NowMs()),
            Record("b", "/w2", NowMs() - 50),
            Record("c", "/w1", NowMs() - 100),
        };
        var entries = NavModel.BuildMainEntries(sessions, NavModel.GroupMode.ByWorkspace);

        await Assert.That(NavModel.IndexForActive(entries, "b")).IsEqualTo(5);
        await Assert.That(NavModel.IndexForActive(entries, "c")).IsEqualTo(3);
    }
}
