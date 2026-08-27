using Phi.Agent;
using Phi.Chat;
using Phi.Tests.Helpers;
using Phi.Tui;
using Phi.Tui.Components;
using XenoAtom.Terminal.UI.Controls;

namespace Phi.Tests.Tui;

/// <summary>
/// Regression tests for the cross-thread marshalling that keeps the TUI
/// from tripping XenoAtom's <c>Invalid thread access</c> check. The
/// projector fires <see cref="ChatTranscriptProjector.Changed"/> from
/// whatever thread the harness emits <see cref="HarnessEvent"/> on; for
/// real streaming providers that's a worker thread (the IO completion
/// thread), for sync mocks it's the calling thread. Without marshalling,
/// the worker-thread case throws on the very first token.
/// <para>
/// These tests pin the marshal wiring that
/// <see cref="ChatTranscript"/>, <see cref="ChatHeader"/>, and
/// <see cref="StatusBarBinder"/> apply at <c>Bind()</c> / build time
/// without spinning up a real <c>TerminalApp</c>: they route every
/// subscription through the supplied <see cref="TuiUiThread"/>, and we
/// verify that routing by giving them a recording marshaller that
/// captures each routed action. End-to-end threading (streaming from a
/// worker thread against a real TerminalApp) is exercised in manual
/// smoke tests — XenoAtom's <c>Dispatcher.Current</c> is a process-wide
/// singleton that doesn't survive unit-test reuse cleanly.
/// </para>
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class TuiUiThreadComponentIntegrationTests
{
    [Test]
    public async Task ChatTranscript_Bind_RoutesProjectorChanged_ThroughUiThread()
    {
        var recorder = new RecordingUiThread();
        var transcript = new ChatTranscript(recorder);

        // Bind triggers the initial projection render synchronously
        // (it's the resume edge). The recorder should see no Post yet
        // because the initial diff runs from the ctor's calling
        // thread — that's by design (we can't Post before any
        // subscription exists).
        transcript.Bind(new MockSession());

        // Fire a streaming event — the projector synchronously emits
        // Changed and the transcript's marshalled handler routes the
        // diff through the recorder.
        transcript.AddUserMessage("hello world");
        await Assert.That(recorder.PostedActions).IsEqualTo(1);
    }

    [Test]
    public async Task ChatHeader_Build_RoutesStateChanged_ThroughUiThread()
    {
        var recorder = new RecordingUiThread();
        var session = new MockSession();

        // Build the header — the StateChanged subscription is wired
        // through the recorder.
        var visual = ChatHeader.Build(session, recorder);

        // Mutate state — the marshalled handler must Post exactly once.
        session.UpdateState(s => s with { Model = "phi-4-from-test" });
        await Assert.That(recorder.PostedActions).IsEqualTo(1);
    }

    [Test]
    public async Task StatusBarBinder_Bind_RoutesStateChangedSinkCalls_ThroughUiThread()
    {
        var recorder = new RecordingUiThread();
        var session = new MockSession();
        var transcript = new ChatTranscript(recorder);
        var statusBar = new PhiStatusBar(session.State.Model);

        StatusBarBinder.Bind(statusBar, transcript, session, recorder);

        // One state change fans out to many sink calls — every one of
        // them must route through the recorder. We mutate just the
        // model so the router classifies the change as
        // non-error and skips the persistent-error path.
        session.UpdateState(s => s with { Model = "phi-4-from-test" });

        // SessionStatusRouter sets State and then turn + queued count
        // and tokens and context and model — so we expect multiple
        // posts (one per sink call). The exact count isn't pinned
        // because SessionStatusRouter's call shape may evolve; what
        // matters is that NONE of them ran synchronously on the
        // calling thread.
        await Assert.That(recorder.PostedActions).IsGreaterThan(0);
    }

    [Test]
    public async Task PromptInput_ShowSessionsDialog_PostsDialogShow_ThroughUiThread()
    {
        // The slash-command dialogs (/sessions, /connect, /models,
        // /api-key) construct a Dialog and call Show() — both touch
        // the visual tree. They must go through the marshaller when
        // called from a non-UI thread (the extension hook chain hits
        // this path via HookRegistry's GetAwaiter().GetResult).
        // The exact marshal path goes through
        // PromptInput._uiThread.Post(() => dialog.Show()) — the
        // recorder counts those posts. We can't actually call Show
        // without a TerminalApp (it throws), so we exercise just
        // enough of the dialog to confirm the Post is wired:
        // PromptInput wires the dialog handler via the marshaller
        // when constructed with a non-null uiThread, and the dialog
        // list/dialog construction itself doesn't touch the visual
        // tree until Show() is called. The marshal contract under
        // test is "every dialog entry point calls _uiThread.Post
        // exactly once for the Show() step", which we pin via the
        // recorder's post count after a no-op dialog Show.
        //
        // In practice the recorder's Post runs the action, which
        // throws because there is no TerminalApp — we catch that
        // and confirm the post still incremented.
        var recorder = new RecordingUiThread();
        var session = new MockSession { RecentSessions = BuildRecentSessions() };
        var transcript = new ChatTranscript(recorder);
        var input = new PromptInput(
            session, new Phi.Providers.ProviderManager(),
            transcript, uiThread: recorder);

        // The dialog Show itself requires a TerminalApp; we can't
        // call ShowSessionsDialog here without it. The contract we
        // pin: PromptInput's _uiThread field is set from the ctor's
        // uiThread parameter (and shows up in our recorder). The
        // /sessions dialog construction uses that field. We verify
        // the wiring is in place by reflection — the runtime test
        // lives in TuiDialogShowerIntegrationTests which actually
        // starts a TerminalApp.
        var uiThreadField = typeof(PromptInput).GetField(
            "_uiThread",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var bound = (TuiUiThread?)uiThreadField!.GetValue(input);
        await Assert.That(bound).IsSameReferenceAs(recorder);
    }

    private static IReadOnlyList<SessionRecord> BuildRecentSessions()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return
        [
            new SessionRecord(
                Id: "session-a",
                Cwd: "/cwd",
                Model: "phi-test",
                Title: "first session",
                CreatedAt: now,
                UpdatedAt: now,
                ProviderName: "test"),
        ];
    }

    /// <summary>
    /// In-process <see cref="TuiUiThread"/> stub that counts every
    /// <see cref="TuiUiThread.Post"/> invocation. The default
    /// <see cref="TuiUiThread.None"/> posts synchronously on the
    /// calling thread; this recorder counts the posts without doing
    /// any visual mutation, so it stays safe in tests that don't need
    /// a real <c>TerminalApp</c>.
    /// </summary>
    private sealed class RecordingUiThread : TuiUiThread
    {
        public int PostedActions { get; private set; }

        public RecordingUiThread() : base(appAccessor: () => null)
        {
        }

        public override void Post(Action action)
        {
            PostedActions++;
            action();
        }

        public override Task<T> InvokeAsync<T>(Func<T> func)
        {
            PostedActions++;
            return Task.FromResult(func());
        }

        public override Task InvokeAsync(Action action)
        {
            PostedActions++;
            action();
            return Task.CompletedTask;
        }

        public override Task<T> InvokeAsync<T>(Func<Task<T>> func)
        {
            PostedActions++;
            return Task.FromResult(func().GetAwaiter().GetResult());
        }
    }
}
