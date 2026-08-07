using PhiCoding.Desk.Components;
using PhiCoding.Desk.Tests.Helpers;

namespace PhiCoding.Desk.Tests.Components;

/// <summary>
/// <see cref="StatusBarView"/>: implements <see cref="PhiCoding.Status.ISessionStatusSink"/>
/// and drives the shared <see cref="PhiCoding.Status.SessionStatusRouter"/> from session
/// state. Assertions target the internal observable-backed text, avoiding the
/// MewUI render loop.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class StatusBarViewTests
{
    [Test]
    public async Task ImplementsStatusSink()
    {
        var view = new StatusBarView();

        await Assert.That(view).IsAssignableTo<PhiCoding.Status.ISessionStatusSink>();
    }

    [Test]
    public async Task SetRunning_UpdatesLeftText()
    {
        var view = new StatusBarView();
        view.SetRunning(true);
        view.SetTurn(3);

        await Assert.That(view.LeftText).IsEqualTo("running · turn 3");
    }

    [Test]
    public async Task SetRunningFalse_QueuedZero_ShowsReady()
    {
        var view = new StatusBarView();
        view.SetRunning(true);
        view.SetTurn(1);
        view.SetRunning(false);
        view.SetQueuedCount(0);

        await Assert.That(view.LeftText).IsEqualTo("ready");
    }

    [Test]
    public async Task SetQueuedCount_Running_ShowsQueuedBadge()
    {
        var view = new StatusBarView();
        view.SetRunning(true);
        view.SetTurn(2);
        view.SetQueuedCount(3);

        await Assert.That(view.LeftText).IsEqualTo("running · turn 2 · +3 queued");
    }

    [Test]
    public async Task UpdateTokens_FormatsCounts()
    {
        var view = new StatusBarView();
        view.UpdateModel("deepseek", "deepseek-v4-flash");
        view.UpdateTokens(1500, 800);

        await Assert.That(view.TokensText).IsEqualTo(" · ↑1.5k ↓800");
    }

    [Test]
    public async Task UpdateContext_WithThreshold_ShowsFraction()
    {
        var view = new StatusBarView();
        view.UpdateContext(2000, 8000);

        var expected = StatusBarView.FormatCount(8000 + PhiCoding.ContextWindow.DefaultCompactionReserveTokens);
        await Assert.That(view.ContextText).IsEqualTo($" · 2.0k/{expected}");
    }

    [Test]
    public async Task UpdateContext_Zero_ShowsEmpty()
    {
        var view = new StatusBarView();
        view.UpdateContext(0, null);

        await Assert.That(view.ContextText).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ShowError_AndClear_TogglesErrorVisible()
    {
        var view = new StatusBarView();
        view.ShowError("Connection timed out after 30s", isPersistent: false);
        await Assert.That(view.ErrorVisible).IsTrue();

        view.ClearError();
        await Assert.That(view.ErrorVisible).IsFalse();
    }

    [Test]
    public async Task BindStatusBar_SubscribesToSessionRouter()
    {
        var session = new MockSession();
        var view = new StatusBarView();
        view.BindStatusBar(session);

        // Simulate a run turn: the router drives the view from session state.
        session.UpdateState(s => s with { IsRunning = true, Turn = 1 });
        await Assert.That(view.LeftText).IsEqualTo("running · turn 1");
    }

    [Test]
    public async Task BindStatusBar_TransientError_ShowsInBar_NotAsPersistentLine()
    {
        var session = new MockSession();
        var view = new StatusBarView();
        view.BindStatusBar(session);

        session.UpdateState(s => s with { LastError = "Connection timed out after 30s" });
        await Assert.That(view.ErrorVisible).IsTrue();
    }

    [Test]
    public async Task FormatCount_SmallNumbers_Raw()
    {
        await Assert.That(StatusBarView.FormatCount(0)).IsEqualTo("0");
        await Assert.That(StatusBarView.FormatCount(999)).IsEqualTo("999");
    }

    [Test]
    public async Task FormatCount_Thousands_KSuffix()
    {
        await Assert.That(StatusBarView.FormatCount(1500)).IsEqualTo("1.5k");
    }

    [Test]
    public async Task ShortenPath_UnderHome_UsesTilde()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await Assert.That(StatusBarView.ShortenPath(home + "/github/phi")).IsEqualTo("~/github/phi");
    }

    [Test]
    public async Task ShortenPath_OutsideHome_Unchanged()
    {
        await Assert.That(StatusBarView.ShortenPath("/var/tmp/x")).IsEqualTo("/var/tmp/x");
    }
}
