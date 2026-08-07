namespace PhiCoding.Desk.Tests;

/// <summary>
/// <see cref="DeskNavModel"/>: pure navigation-model logic for the desktop
/// shell's left pane (entry ordering, workspace grouping, active highlight,
/// and the Expanded/Compact pane-mode toggle).
/// </summary>
public class DeskNavModelTests
{
    private static SessionRecord Record(string id, string? title = null, string cwd = "/cwd", long updatedAt = 0) =>
        new(id, cwd, "m", title, updatedAt, updatedAt);

    [Test]
    public async Task BuildMainEntries_Expanded_NewChatFirst_ThenToggleRow()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [],
            DeskNavModel.GroupMode.ByWorkspace,
            DeskNavModel.PaneMode.Expanded);

        await Assert.That(entries[0].Kind).IsEqualTo(DeskNavModel.Kind.NewChat);
        await Assert.That(entries[1].Kind).IsEqualTo(DeskNavModel.Kind.ToggleRow);
        await Assert.That(entries[1].Title).IsEqualTo("By workspace ▾");
        await Assert.That(entries[1].ToggleMode).IsEqualTo(DeskNavModel.GroupMode.ByWorkspace);
    }

    [Test]
    public async Task BuildMainEntries_Compact_ReturnsOnlyNewChat()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [Record("a"), Record("b")],
            DeskNavModel.GroupMode.ByWorkspace,
            DeskNavModel.PaneMode.Compact);

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Kind).IsEqualTo(DeskNavModel.Kind.NewChat);
    }

    [Test]
    public async Task BuildMainEntries_Compact_DoesNotIncludeToggleRow()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [Record("a")],
            DeskNavModel.GroupMode.ByDate,
            DeskNavModel.PaneMode.Compact);

        await Assert.That(entries.Any(e => e.Kind == DeskNavModel.Kind.ToggleRow)).IsFalse();
    }

    [Test]
    public async Task BuildMainEntries_ToggleRowLabel_ReflectsByDateMode()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [],
            DeskNavModel.GroupMode.ByDate,
            DeskNavModel.PaneMode.Expanded);

        await Assert.That(entries[1].Title).IsEqualTo("By date ▾");
        await Assert.That(entries[1].ToggleMode).IsEqualTo(DeskNavModel.GroupMode.ByDate);
    }

    [Test]
    public async Task BuildMainEntries_ByDate_FlattensSessionsNewestFirst()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [
                Record("a", updatedAt: 100),
                Record("b", updatedAt: 500),
                Record("c", updatedAt: 300),
            ],
            DeskNavModel.GroupMode.ByDate,
            DeskNavModel.PaneMode.Expanded);

        // indices: 0 NewChat, 1 ToggleRow, 2..4 sessions
        var sessionIds = entries
            .Where(e => e.Kind == DeskNavModel.Kind.Session)
            .Select(e => e.SessionId!)
            .ToArray();
        await Assert.That(sessionIds).IsEquivalentTo(["b", "c", "a"]);
        // No workspace headers in ByDate mode.
        await Assert.That(entries.Any(e => e.Kind == DeskNavModel.Kind.Workspace)).IsFalse();
    }

    [Test]
    public async Task BuildMainEntries_ByWorkspace_GroupsAndOrders()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [
                Record("a1", cwd: "/old", updatedAt: 100),
                Record("a2", cwd: "/old", updatedAt: 50),
                Record("b1", cwd: "/new", updatedAt: 500),
            ],
            DeskNavModel.GroupMode.ByWorkspace,
            DeskNavModel.PaneMode.Expanded);

        // Newest workspace first → /new, then /old; within /old, a1 (100) before a2 (50).
        var kindsAndTitles = entries.Select(e => (e.Kind, e.Title)).ToArray();
        await Assert.That(kindsAndTitles).IsEquivalentTo(new[]
        {
            (DeskNavModel.Kind.NewChat, "New Chat"),
            (DeskNavModel.Kind.ToggleRow, "By workspace ▾"),
            (DeskNavModel.Kind.Workspace, "/new"),
            (DeskNavModel.Kind.Session, "b1"),
            (DeskNavModel.Kind.Workspace, "/old"),
            (DeskNavModel.Kind.Session, "a1"),
            (DeskNavModel.Kind.Session, "a2"),
        });
    }

    [Test]
    public async Task BuildMainEntries_SessionTitle_FallsBackToIdPrefix()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [Record("0123456789abcdef", title: null)],
            DeskNavModel.GroupMode.ByDate,
            DeskNavModel.PaneMode.Expanded);

        var sessionEntry = entries.Single(e => e.Kind == DeskNavModel.Kind.Session);
        await Assert.That(sessionEntry.SessionId).IsEqualTo("0123456789abcdef");
        await Assert.That(sessionEntry.Title).IsEqualTo("01234567");
    }

    [Test]
    public async Task BuildMainEntries_SessionTitle_UsesTitleWhenPresent()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [Record("0123456789abcdef", title: "Refactor parser")],
            DeskNavModel.GroupMode.ByDate,
            DeskNavModel.PaneMode.Expanded);

        var sessionEntry = entries.Single(e => e.Kind == DeskNavModel.Kind.Session);
        await Assert.That(sessionEntry.Title).IsEqualTo("Refactor parser");
    }

    [Test]
    public async Task GroupByWorkspace_OrdersGroupsByNewestSession()
    {
        var groups = DeskNavModel.GroupByWorkspace([
            Record("old", cwd: "/old", updatedAt: 100),
            Record("new", cwd: "/new", updatedAt: 500),
        ]);

        await Assert.That(groups[0].Workspace).IsEqualTo("/new");
        await Assert.That(groups[1].Workspace).IsEqualTo("/old");
    }

    [Test]
    public async Task GroupByWorkspace_SortsSessionsNewestFirstWithinGroup()
    {
        var groups = DeskNavModel.GroupByWorkspace([
            Record("older", cwd: "/w", updatedAt: 100),
            Record("newer", cwd: "/w", updatedAt: 500),
        ]);

        var ids = groups[0].Sessions.Select(s => s.Id).ToArray();
        await Assert.That(ids[0]).IsEqualTo("newer");
        await Assert.That(ids[1]).IsEqualTo("older");
    }

    [Test]
    public async Task WorkspaceLabel_ShortensHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await Assert.That(DeskNavModel.WorkspaceLabel(home + "/github/phi")).IsEqualTo("~/github/phi");
        await Assert.That(DeskNavModel.WorkspaceLabel("/var/tmp/x")).IsEqualTo("/var/tmp/x");
    }

    [Test]
    public async Task BuildFooterEntries_ContainsModelsAndProviders()
    {
        var footer = DeskNavModel.BuildFooterEntries();

        await Assert.That(footer.Select(e => e.Kind))
            .IsEquivalentTo([DeskNavModel.Kind.Models, DeskNavModel.Kind.Providers]);
    }

    [Test]
    public async Task IndexForActive_UnpersistedSession_ReturnsNewChat()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [Record("a"), Record("b")],
            DeskNavModel.GroupMode.ByWorkspace,
            DeskNavModel.PaneMode.Expanded);

        await Assert.That(DeskNavModel.IndexForActive(entries, null)).IsEqualTo(0);
        await Assert.That(DeskNavModel.IndexForActive(entries, "unknown")).IsEqualTo(0);
    }

    [Test]
    public async Task IndexForActive_MatchingSession_ReturnsItsRow()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [Record("a"), Record("b")],
            DeskNavModel.GroupMode.ByWorkspace,
            DeskNavModel.PaneMode.Expanded);

        // indices: 0 NewChat, 1 ToggleRow, 2 workspace, 3 a, 4 b
        await Assert.That(DeskNavModel.IndexForActive(entries, "a")).IsEqualTo(3);
        await Assert.That(DeskNavModel.IndexForActive(entries, "b")).IsEqualTo(4);
    }

    [Test]
    public async Task IndexForActive_Compact_OnlyNewChatIsValid()
    {
        var entries = DeskNavModel.BuildMainEntries(
            [Record("a")],
            DeskNavModel.GroupMode.ByWorkspace,
            DeskNavModel.PaneMode.Compact);

        await Assert.That(DeskNavModel.IndexForActive(entries, "a")).IsEqualTo(0);
        await Assert.That(DeskNavModel.IndexForActive(entries, null)).IsEqualTo(0);
    }
}