using PhiCoding.Desk;

namespace PhiCoding.Desk.Tests;

/// <summary>
/// <see cref="DeskNavModel"/>: pure navigation-model logic for the desktop
/// shell's left pane (entry ordering, session titles, active highlight).
/// </summary>
public class DeskNavModelTests
{
    private static SessionRecord Record(string id, string? title = null) =>
        new(id, "/cwd", "m", title, 0, 0);

    [Test]
    public async Task BuildMainEntries_NewChatFirst_ThenSessionsHeader()
    {
        var entries = DeskNavModel.BuildMainEntries([]);

        await Assert.That(entries[0].Kind).IsEqualTo(DeskNavModel.Kind.NewChat);
        await Assert.That(entries[1].Kind).IsEqualTo(DeskNavModel.Kind.Header);
        await Assert.That(entries[1].Title).IsEqualTo("Sessions");
    }

    [Test]
    public async Task BuildMainEntries_SessionTitle_FallsBackToIdPrefix()
    {
        var entries = DeskNavModel.BuildMainEntries([Record("0123456789abcdef", title: null)]);

        var sessionEntry = entries.Single(e => e.Kind == DeskNavModel.Kind.Session);
        await Assert.That(sessionEntry.SessionId).IsEqualTo("0123456789abcdef");
        await Assert.That(sessionEntry.Title).IsEqualTo("01234567");
    }

    [Test]
    public async Task BuildMainEntries_SessionTitle_UsesTitleWhenPresent()
    {
        var entries = DeskNavModel.BuildMainEntries([Record("0123456789abcdef", title: "Refactor parser")]);

        var sessionEntry = entries.Single(e => e.Kind == DeskNavModel.Kind.Session);
        await Assert.That(sessionEntry.Title).IsEqualTo("Refactor parser");
    }

    [Test]
    public async Task BuildMainEntries_Sessions_AppearInOrder()
    {
        var entries = DeskNavModel.BuildMainEntries([Record("a"), Record("b")]);

        var sessionIds = entries
            .Where(e => e.Kind == DeskNavModel.Kind.Session)
            .Select(e => e.SessionId!)
            .ToArray();
        await Assert.That(sessionIds).IsEquivalentTo(["a", "b"]);
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
        var entries = DeskNavModel.BuildMainEntries([Record("a"), Record("b")]);

        await Assert.That(DeskNavModel.IndexForActive(entries, null)).IsEqualTo(0);
        await Assert.That(DeskNavModel.IndexForActive(entries, "unknown")).IsEqualTo(0);
    }

    [Test]
    public async Task IndexForActive_MatchingSession_ReturnsItsRow()
    {
        var entries = DeskNavModel.BuildMainEntries([Record("a"), Record("b")]);

        // indices: 0 New Chat, 1 Sessions header, 2 = a, 3 = b
        await Assert.That(DeskNavModel.IndexForActive(entries, "a")).IsEqualTo(2);
        await Assert.That(DeskNavModel.IndexForActive(entries, "b")).IsEqualTo(3);
    }
}