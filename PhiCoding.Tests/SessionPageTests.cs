using PhiCoding.Tui.Pages;
using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Tests.Helpers;
using PhiCoding.Tui;
using PhiCoding.Tui.Components;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tests;

/// <summary>
/// <see cref="SessionPage"/>: the detail page for <c>/sessions/:id</c>,
/// including the pending-submission bubble rendered when a fresh new-session
/// page promotes mid-run.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class SessionPageTests
{
    private static SessionPage BuildPage(MockSession session, FakeSessionNavigator navigator)
    {
        var page = new SessionPage(session, navigator, new ProviderManager());
        page.Build();
        return page;
    }

    private static int ItemCount(ChatTranscript transcript) =>
        ((DocumentFlow)transcript.Visual).Items.Count;

    [Test]
    public async Task Build_WithPendingSubmission_AndRunningSession_RendersUserBubble()
    {
        // The first prompt on the landing page starts a run and promotes;
        // the detail page must render the submitted text as the user bubble
        // and consume the pending submission.
        var session = new MockSession();
        session.UpdateState(s => s with { IsRunning = true });
        var navigator = new FakeSessionNavigator(
            session, new ChatRoute(new ExistingSessionRequest("x")));
        navigator.SetPendingSubmission("first prompt");

        var page = BuildPage(session, navigator);

        await Assert.That(ItemCount(page.Transcript)).IsEqualTo(1);
        await Assert.That(navigator.TakePendingSubmission()).IsNull();
    }

    [Test]
    public async Task Build_WithPendingSubmission_ButIdleSession_DoesNotRenderBubble()
    {
        // The run already settled before the page mounted: the session's
        // State renders the messages, so the pending text must not duplicate
        // the bubble.
        var session = new MockSession();
        var navigator = new FakeSessionNavigator(
            session, new ChatRoute(new ExistingSessionRequest("x")));
        navigator.SetPendingSubmission("first prompt");

        var page = BuildPage(session, navigator);

        await Assert.That(ItemCount(page.Transcript)).IsEqualTo(0);
        await Assert.That(navigator.TakePendingSubmission()).IsNull();
    }

    [Test]
    public async Task Build_NoPendingSubmission_RendersOnlySessionMessages()
    {
        var session = new MockSession();
        var navigator = new FakeSessionNavigator(
            session, new ChatRoute(new ExistingSessionRequest("x")));

        var page = BuildPage(session, navigator);

        await Assert.That(ItemCount(page.Transcript)).IsEqualTo(0);
    }
}
