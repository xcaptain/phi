using PhiCoding.Providers;
using PhiCoding.Tests.Helpers;
using PhiCoding.Tui.Pages;

namespace PhiCoding.Tests;

/// <summary>
/// Error routing in <see cref="SessionPage"/>: the status-bar ↔ transcript
/// pipeline dedups repeated identical errors between runs and resets the
/// dedup state when the underlying <c>LastError</c> clears.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class SessionPageErrorRoutingTests
{
    private static (MockSession Session, SessionPage Page) CreatePage()
    {
        var session = new MockSession();
        var page = new SessionPage(session, new FakeSessionNavigator(session), new ProviderManager());
        page.Build();
        return (session, page);
    }

    private static int ItemCount(SessionPage page)
        => page.Transcript.Flow.Items.Count;

    [Test]
    public async Task SameErrorFiredMultipleTimes_IsDedupedToOneTranscriptLine()
    {
        // One persistent error arrives, then the same error fires several
        // more times on subsequent StateChanged events before the next run
        // clears LastError. The transcript must record exactly one line.
        var (session, page) = CreatePage();

        session.UpdateState(s => s with { LastError = "401 Unauthorized" });
        session.UpdateState(s => s with { LastError = "401 Unauthorized" });
        session.UpdateState(s => s with { LastError = "401 Unauthorized" });

        await Assert.That(ItemCount(page)).IsEqualTo(1);
    }

    [Test]
    public async Task NewRunClearingLastError_AllowsNextOccurrenceToBeRecorded()
    {
        // First run fails persistently → one transcript line. The next run
        // starts → LastError clears. A subsequent failure with the SAME
        // message must produce a fresh transcript record (dedup state resets
        // so repeated failures across runs stay visible).
        var (session, page) = CreatePage();

        session.UpdateState(s => s with { LastError = "Model not found: phi-99" });
        session.UpdateState(s => s with { LastError = null });
        session.UpdateState(s => s with { LastError = "Model not found: phi-99" });

        await Assert.That(ItemCount(page)).IsEqualTo(2);
    }

    [Test]
    public async Task LastErrorCleared_RestoresStatusBarToModelDisplay()
    {
        var (session, page) = CreatePage();

        session.UpdateState(s => s with { Model = "phi-3", LastError = "boom" });
        await Assert.That(page.StatusBar.CurrentError).IsNotNull();

        // Next run starts: status bar must clear, no new transcript item.
        var itemCountBefore = ItemCount(page);
        session.UpdateState(s => s with { LastError = null });
        await Assert.That(page.StatusBar.CurrentError).IsNull();
        await Assert.That(ItemCount(page)).IsEqualTo(itemCountBefore);
    }

    [Test]
    public async Task TransientError_NeverEntersTranscript()
    {
        var (session, page) = CreatePage();

        session.UpdateState(s => s with { LastError = "Connection timed out after 30s" });
        session.UpdateState(s => s with { LastError = "429 rate limit exceeded" });

        await Assert.That(ItemCount(page)).IsEqualTo(0);
    }
}
