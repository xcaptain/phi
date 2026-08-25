using Phi.Providers;
using Phi.Tests.Helpers;
using Phi.Tui;
using Phi.Tui.Components;
using XenoAtom.Terminal.UI.Controls;
using Phi.Extensions;
using Phi.Extensions.Host;

namespace Phi.Tests;

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
        var app = new PhiTuiApp(session, new ProviderManager());

        var root = app.BuildRoot();

        await Assert.That(root).IsNotNull();
        await Assert.That(root).IsTypeOf<ComputedVisual>();
    }

    [Test]
    public async Task TwoArgCtor_SetsSinkAndDialogFallback_BuildsPageWithoutThrowing()
    {
        // Regression: the 2-arg ctor used to overwrite its own null-fallback
        // assignments with the raw (null) parameters, leaving _dialogShower
        // and _onSinkBuilt null. The next BuildCurrentPage call then NRE'd
        // when constructing TuiUiSink. The ComputedVisual returned by
        // BuildRoot defers BuildCurrentPage until render, so a test that
        // only inspects the root won't catch this — we invoke the page
        // builder directly via reflection to lock in the fix.
        var session = new MockSession();
        var app = new PhiTuiApp(session, new ProviderManager());

        var page = typeof(PhiTuiApp)
            .GetMethod("BuildCurrentPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(app, null);

        await Assert.That(page).IsNotNull();
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

    [Test]
    public async Task PromptInput_ExtensionSlashCommand_Dispatched_HandlerResultShownAsTransient()
    {
        // Direct PromptInput dispatch test — bypasses PhiTuiApp's reflection
        // path entirely and exercises the closure shape PhiTuiApp constructs
        // (Func<string, string, string?> with the live IPhiContext captured
        // by the runtime). The runtime-level dispatch is covered by
        // SlashCommandDispatcherTests; this test pins the wiring between
        // PromptInput and the dispatch closure.
        var session = new MockSession();
        var transcript = new ChatTranscript();
        var providers = new ProviderManager();

        var runtime = new Phi.Extensions.Host.ExtensionRuntime(session, new NullPhiUiBridge());
        try
        {
            runtime.RegisterCompiledExtension(new CapturingSlashExt(api =>
                api.RegisterCommand("/hello",
                    (args, _) => $"hi {args}",
                    description: "demo")));
            runtime.Initialize();

            // Mirror what PhiTuiApp.BuildCurrentPage does: build a closure
            // that ignores the per-call ctx and uses the runtime's cached one.
            Phi.Extensions.IPhiContext? ctx = runtime.Context;
            Func<string, string, string?> dispatcher = (name, args) =>
                runtime.TryDispatch(name, args, ctx!, out var msg) ? msg : null;

            var input = new PromptInput(
                session, providers, transcript,
                commands: [new Phi.Slash.SlashCommandDef("/hello", "demo")],
                dispatcher: dispatcher);
            input.Build();

            // Submit via the same path the editor's Accepted handler uses:
            // locate HandleInput and invoke it directly. This is the path
            // exercised by "/hello world" → dispatcher → "hi world".
            var handleInput = typeof(PromptInput)
                .GetMethod("HandleInput",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            handleInput.Invoke(input, ["/hello world"]);

            // Slash command results surface as a transient line (the bottom
            // status region of the transcript), not a persisted flow row.
            await Assert.That(transcript.TransientText).IsEqualTo("hi world");
        }
        finally
        {
            runtime.Dispose();
        }
    }

    /// <summary>Mirrors the extension registration pattern used in
    /// <see cref="TranscriptLineSubmissionTests"/>: a one-shot
    /// <see cref="Phi.Extensions.IPhiExtension"/> that captures its
    /// <see cref="Phi.Extensions.IPhiApi"/> for the test to drive.</summary>
    [PhiExtension(
        Name = "slash-dispatch-fixture",
        Version = "1.0.0",
        Description = "Test extension that registers one slash command for the dispatch test.",
        Capabilities = ExtensionCapability.UiInteract)]
    private sealed class CapturingSlashExt(Action<IPhiApi> onSetup) : IPhiExtension
    {
        public void Setup(IPhiApi api) => onSetup(api);
    }
}
