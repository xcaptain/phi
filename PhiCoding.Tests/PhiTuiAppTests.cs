using PhiCoding.Providers;
using PhiCoding.Tests.Helpers;
using PhiCoding.Tui;
using PhiCoding.Tui.Components;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tests;

/// <summary>
/// <see cref="PhiTuiApp"/>: the chat shell. <see cref="StatusBarBinder"/> —
/// the status-bar ↔ session state wiring — is the part that's worth testing
/// independently of the live TUI; the shell itself just builds a host.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class PhiTuiAppTests
{
    [Test]
    public async Task BuildRoot_ReturnsHost_ForCurrentRoute()
    {
        var session = new MockSession();
        var app = new PhiTuiApp(new FakeSessionNavigator(session), new ProviderManager());

        var root = app.BuildRoot();

        await Assert.That(root).IsNotNull();
        await Assert.That(root).IsTypeOf<ComputedVisual>();
    }

    [Test]
    public async Task StatusBarBinder_SameErrorFiredMultipleTimes_IsDedupedToOneTranscriptLine()
    {
        // One persistent error arrives, then the same error fires several
        // more times on subsequent StateChanged events before the next run
        // clears LastError. The transcript must record exactly one line.
        var session = new MockSession();
        var transcript = new ChatTranscript();
        var status = new PhiStatusBar(session.State.Model);
        StatusBarBinder.Bind(status, transcript, session);

        session.UpdateState(s => s with { LastError = "401 Unauthorized" });
        session.UpdateState(s => s with { LastError = "401 Unauthorized" });
        session.UpdateState(s => s with { LastError = "401 Unauthorized" });

        await Assert.That(transcript.Flow.Items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task StatusBarBinder_NewRunClearingLastError_AllowsNextOccurrenceToBeRecorded()
    {
        var session = new MockSession();
        var transcript = new ChatTranscript();
        var status = new PhiStatusBar(session.State.Model);
        StatusBarBinder.Bind(status, transcript, session);

        session.UpdateState(s => s with { LastError = "Model not found: phi-99" });
        session.UpdateState(s => s with { LastError = null });
        session.UpdateState(s => s with { LastError = "Model not found: phi-99" });

        await Assert.That(transcript.Flow.Items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task StatusBarBinder_LastErrorCleared_RestoresStatusBarToModelDisplay()
    {
        var session = new MockSession();
        var transcript = new ChatTranscript();
        var status = new PhiStatusBar(session.State.Model);
        StatusBarBinder.Bind(status, transcript, session);

        session.UpdateState(s => s with { Model = "phi-3", LastError = "boom" });
        await Assert.That(status.CurrentError).IsNotNull();

        var itemCountBefore = transcript.Flow.Items.Count;
        session.UpdateState(s => s with { LastError = null });
        await Assert.That(status.CurrentError).IsNull();
        await Assert.That(transcript.Flow.Items.Count).IsEqualTo(itemCountBefore);
    }

    [Test]
    public async Task StatusBarBinder_TransientError_NeverEntersTranscript()
    {
        var session = new MockSession();
        var transcript = new ChatTranscript();
        var status = new PhiStatusBar(session.State.Model);
        StatusBarBinder.Bind(status, transcript, session);

        session.UpdateState(s => s with { LastError = "Connection timed out after 30s" });
        session.UpdateState(s => s with { LastError = "429 rate limit exceeded" });

        await Assert.That(transcript.Flow.Items.Count).IsEqualTo(0);
    }
}
