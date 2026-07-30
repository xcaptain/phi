using PhiAgent;
using PhiCoding.Tests.Helpers;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

/// <summary>
/// Runtime behavior of <see cref="CodingSession"/>: lazy persistence
/// (a fresh session writes nothing until its first message), provider
/// injection via <see cref="SessionConfig"/>, per-message durability
/// during a run, and adopt-replacement resume semantics.
/// </summary>
[NotInParallel("session-tests")]
public class CodingSessionRuntimeTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;

    public CodingSessionRuntimeTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), "phi-runtime-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
        _phiHome = Path.Combine(Path.GetTempPath(), "phi-home-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PHI_HOME", _phiHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PHI_HOME", null);
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_phiHome)) Directory.Delete(_phiHome, recursive: true);
    }

    private SessionConfig ConfigWith(IPhiProvider provider) => new()
    {
        Cwd = _cwd,
        Provider = provider,
        Model = "stub-model",
        SystemPrompt = "test",
        MaxTurns = 5,
        Tools = [],
    };

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20);
        }
    }

    // ──────────────────── Lazy persistence ────────────────────

    [Test]
    public async Task Create_FreshSession_WritesNothingToDisk()
    {
        var session = CodingSession.Create(_cwd, "m");

        await Assert.That(session.IsPersisted).IsFalse();
        await Assert.That(File.Exists(SessionPaths.IndexFileFor(_cwd))).IsFalse();
        await Assert.That(
            File.Exists(SessionPaths.SessionFileFor(_cwd, session.Id))).IsFalse();
        await Assert.That(new SessionManager(_cwd).ListSessions()).IsEmpty();
    }

    [Test]
    public async Task AppendMessage_FirstCall_PersistsIndexAndTranscript()
    {
        var session = CodingSession.Create(_cwd, "m");

        session.AppendMessage(new UserMessage { Content = "hi" });

        await Assert.That(session.IsPersisted).IsTrue();
        var manager = new SessionManager(_cwd);
        await Assert.That(manager.FindSession(session.Id)).IsNotNull();
        await Assert.That(File.Exists(manager.SessionFileFor(session.Id))).IsTrue();
    }

    [Test]
    public async Task Rename_UnpersistedSession_PersistsWithTitle()
    {
        var session = CodingSession.Create(_cwd, "m");

        session.Rename("titled");

        await Assert.That(session.IsPersisted).IsTrue();
        await Assert.That(
            new SessionManager(_cwd).GetSession(session.Id).Title).IsEqualTo("titled");
    }

    // ──────────────────── Provider injection via SessionConfig ────────────────────

    [Test]
    public async Task Create_WithConfig_UsesInjectedProvider()
    {
        var provider = StubProvider.Echo(StubProvider.TextTurn("pong"));
        var session = CodingSession.Create(ConfigWith(provider));

        session.SubmitPrompt("ping");

        await WaitForAsync(() =>
            session.State.Messages.OfType<AssistantMessage>().Any(m => m.Text == "pong"));
        await Assert.That(provider.CallCount).IsGreaterThan(0);
    }

    [Test]
    public async Task StartRuntime_StateCarriesIdentityAndPersistenceFlag()
    {
        var session = CodingSession.Create(ConfigWith(StubProvider.Echo(StubProvider.TextTurn("x"))));

        await Assert.That(session.State.SessionId).IsEqualTo(session.Id);
        await Assert.That(session.State.Model).IsEqualTo("stub-model");
        await Assert.That(session.State.IsPersisted).IsFalse();
    }

    // ──────────────────── Per-message durability ────────────────────

    [Test]
    public async Task SubmitPrompt_UserMessageHitsDiskBeforeResponseCompletes()
    {
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var session = CodingSession.Create(ConfigWith(StubProvider.FirstCallBlocks(gate)));

        session.SubmitPrompt("hello world");

        // The model is still "thinking" (gate not released), but the user
        // message must already be durable — a crash now must not lose it.
        var manager = new SessionManager(_cwd);
        var file = manager.SessionFileFor(session.Id);
        await WaitForAsync(() =>
            File.Exists(file) && File.ReadAllText(file).Contains("hello world"));
        await Assert.That(session.IsPersisted).IsTrue();

        session.Cancel();
        await WaitForAsync(() => !session.State.IsRunning);
    }

    [Test]
    public async Task SubmitPrompt_CompletedRun_FlushesAssistantMessage()
    {
        var session = CodingSession.Create(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("done"))));

        session.SubmitPrompt("go");

        await WaitForAsync(() =>
            session.State.Messages.OfType<AssistantMessage>().Any(m => m.Text == "done"));
        await WaitForAsync(() => !session.State.IsRunning);

        var loaded = CodingSession.Resume(session.Id, _cwd).LoadMessages();
        await Assert.That(loaded.OfType<UserMessage>().Any(m => m.Text == "go")).IsTrue();
        await Assert.That(
            loaded.OfType<AssistantMessage>().Any(m => m.Text == "done")).IsTrue();
    }

    // ──────────────────── Resume (adopt-replacement) ────────────────────

    [Test]
    public async Task ResumeSession_SwapsMessagesAndIdentity()
    {
        var session = CodingSession.Create(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))));

        var target = CodingSession.Create(_cwd, "m", title: "target");
        target.AppendMessage(new UserMessage { Content = "old conversation" });

        await ((ISession)session).ResumeSession(target.Id);

        await Assert.That(session.State.SessionId).IsEqualTo(target.Id);
        await Assert.That(session.State.SessionTitle).IsEqualTo("target");
        await Assert.That(session.State.Messages.Count).IsEqualTo(1);
        await Assert.That(
            ((UserMessage)session.State.Messages[0]).Text).IsEqualTo("old conversation");
    }

    [Test]
    public async Task ResumeSession_NewAppendsLandInTargetFile()
    {
        // Regression: previously resume swapped the record but kept the old
        // storage, leaking new messages into the previous session's file.
        var session = CodingSession.Create(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))));
        session.AppendMessage(new UserMessage { Content = "mine" });
        var originalId = session.Id;

        var target = CodingSession.Create(_cwd, "m");
        target.AppendMessage(new UserMessage { Content = "theirs" });

        await ((ISession)session).ResumeSession(target.Id);
        session.AppendMessage(new UserMessage { Content = "after resume" });

        var targetMessages = CodingSession.Resume(target.Id, _cwd).LoadMessages();
        await Assert.That(targetMessages.OfType<UserMessage>().Select(m => m.Text))
            .IsEquivalentTo(["theirs", "after resume"]);

        var originalMessages = CodingSession.Resume(originalId, _cwd).LoadMessages();
        await Assert.That(originalMessages.OfType<UserMessage>().Select(m => m.Text))
            .IsEquivalentTo(["mine"]);
    }

    [Test]
    public async Task ResumeSession_WhileRunning_CancelsRunThenSwitches()
    {
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionA = CodingSession.Create(
            ConfigWith(StubProvider.FirstCallBlocks(gate, "unblocked")));
        var sessionA_originalId = sessionA.Id;
        var sessionB = CodingSession.Create(_cwd, "m");
        sessionB.AppendMessage(new UserMessage { Content = "existing b" });

        sessionA.SubmitPrompt("prompt a");
        await WaitForAsync(() => sessionA.State.IsRunning);

        await ((ISession)sessionA).ResumeSession(sessionB.Id);

        await Assert.That(sessionA.State.IsRunning).IsFalse();
        await Assert.That(sessionA.State.SessionId).IsEqualTo(sessionB.Id);

        // Prompt A flushed to old session file before cancel
        var manager = new SessionManager(_cwd);
        var oldFile = manager.SessionFileFor(sessionA_originalId);
        await Assert.That(File.ReadAllText(oldFile).Contains("prompt a")).IsTrue();

        // Session B file unchanged by the resume
        var bMessages = CodingSession.Resume(sessionB.Id, _cwd).LoadMessages();
        await Assert.That(bMessages.OfType<UserMessage>().Select(m => m.Text))
            .IsEquivalentTo(["existing b"]);
    }

    [Test]
    public async Task ResumeSession_UnknownId_SetsErrorState()
    {
        var session = CodingSession.Create(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))));

        await ((ISession)session).ResumeSession("does-not-exist");

        await Assert.That(session.State.LastError).IsNotNull();
    }

    [Test]
    public async Task Resume_WithConfig_LoadsTranscriptIntoState()
    {
        var stored = CodingSession.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "from disk" });

        var session = CodingSession.Resume(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))), stored.Id);

        await Assert.That(session.State.SessionId).IsEqualTo(stored.Id);
        await Assert.That(session.State.Messages.Count).IsEqualTo(1);
        await Assert.That(
            ((UserMessage)session.State.Messages[0]).Text).IsEqualTo("from disk");
        await Assert.That(session.IsPersisted).IsTrue();
    }
}
