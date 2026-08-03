using PhiAgent;
using PhiCoding.Prompts;
using PhiCoding.Sessions;
using PhiCoding.Tests.Helpers;

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
    private readonly PhiCoding.Providers.ProviderManager _providerManager = new();
    private readonly CodingSessionFactory _factory;

    public CodingSessionRuntimeTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), "phi-runtime-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
        _phiHome = Path.Combine(Path.GetTempPath(), "phi-home-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PHI_HOME", _phiHome);
        _factory = new CodingSessionFactory(_providerManager);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("PHI_HOME", null);
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_phiHome)) Directory.Delete(_phiHome, recursive: true);
    }

    private SessionConfig ConfigWith(IPhiProvider provider) => new()
    {
        Cwd = _cwd,
        Provider = provider,
        Model = "stub-model",
        SystemPrompt = new SystemPromptOptions { ResolvedSystemPrompt = "test" },
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
        var session = _factory.Create(ConfigWith(provider));

        session.SubmitPrompt("ping");

        await WaitForAsync(() =>
            session.State.Messages.OfType<AssistantMessage>().Any(m => m.Text == "pong"));
        await Assert.That(provider.CallCount).IsGreaterThan(0);
    }

    [Test]
    public async Task StartRuntime_StateCarriesIdentityAndPersistenceFlag()
    {
        var session = _factory.Create(ConfigWith(StubProvider.Echo(StubProvider.TextTurn("x"))));

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
        var session = _factory.Create(ConfigWith(StubProvider.FirstCallBlocks(gate)));

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
        var session = _factory.Create(
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
        var session = _factory.Create(
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
        var session = _factory.Create(
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
        var sessionA = _factory.Create(
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
        var session = _factory.Create(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))));

        await ((ISession)session).ResumeSession("does-not-exist");

        await Assert.That(session.State.LastError).IsNotNull();
    }

    [Test]
    public async Task NewSession_StartsFreshEmptyUnpersistedSession()
    {
        var session = _factory.Create(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))));
        session.SubmitPrompt("first conversation");
        await WaitForAsync(() => !session.State.IsRunning);
        var oldId = session.Id;
        await Assert.That(session.State.Messages).IsNotEmpty();

        await ((ISession)session).NewSession();

        await Assert.That(session.Id).IsNotEqualTo(oldId);
        await Assert.That(session.State.SessionId).IsEqualTo(session.Id);
        await Assert.That(session.State.Messages).IsEmpty();
        await Assert.That(session.State.IsPersisted).IsFalse();
        // Provider/model carry over: the user stays connected, just a
        // blank conversation.
        await Assert.That(session.State.Model).IsEqualTo("stub-model");
        await Assert.That(File.Exists(SessionPaths.SessionFileFor(_cwd, session.Id))).IsFalse();
    }

    [Test]
    public async Task NewSession_CanSubmitPromptAfterwards()
    {
        var session = _factory.Create(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))));
        session.SubmitPrompt("old");
        await WaitForAsync(() => !session.State.IsRunning);

        await ((ISession)session).NewSession();

        session.SubmitPrompt("new");
        await WaitForAsync(() => !session.State.IsRunning);

        await Assert.That(session.State.Messages.OfType<UserMessage>().Any(u => u.Text == "new")).IsTrue();
        await Assert.That(session.State.Messages.OfType<UserMessage>().Any(u => u.Text == "old")).IsFalse();
    }

    [Test]
    public async Task LoadSkill_AppendsBodyAsUserMessage_AndPersists()
    {
        // Project-root skills come from <projectRoot>/.agents/skills — a
        // .git marker makes the temp dir the project root, so the factory
        // finds the skill without touching the real home dir.
        var projectRoot = Path.GetFullPath(Path.Combine(_cwd, "..", "proj-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(projectRoot, ".git"));
        var skillDir = Path.GetFullPath(Path.Combine(projectRoot, ".agents", "skills", "dotnet-testing"));
        Directory.CreateDirectory(skillDir);
        var body = "Test the dotnet code with xUnit.\nUse references/xunit.md for patterns.";
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
            $"---\nname: dotnet-testing\ndescription: Write xUnit tests\n---\n{body}\n");

        var config = ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))) with
        {
            Cwd = projectRoot,
        };
        var session = _factory.Create(config);

        await ((ISession)session).LoadSkillAsync("dotnet-testing");

        var user = session.State.Messages.OfType<UserMessage>().FirstOrDefault();
        await Assert.That(user).IsNotNull();
        await Assert.That(user!.Text).Contains("Test the dotnet code with xUnit.");
        // The message anchors the skill's directory so the model can resolve
        // relative references (references/, scripts/) to absolute paths.
        await Assert.That(user.Text).Contains(skillDir);
        await Assert.That(user.Text).Contains("dotnet-testing");
        // Persisted: the message survives a reload from disk.
        var reloaded = CodingSession.Resume(session.Id, projectRoot).LoadMessages();
        await Assert.That(reloaded.OfType<UserMessage>().Any(u => u.Text.Contains("xUnit"))).IsTrue();
    }

    [Test]
    public async Task LoadSkill_UnknownSkill_Throws()
    {
        var session = _factory.Create(ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))));

        var ex = await Assert.That(async () => await ((ISession)session).LoadSkillAsync("nope"))
            .Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("nope");
    }

    [Test]
    public async Task NewRun_ClearsPreviousLastError()
    {
        // First run fails (LastError set); the next run must start with a
        // clean LastError so the status bar can restore its normal display
        // and a fresh failure leaves a new record.
        var session = _factory.Create(ConfigWith(StubProvider.FirstTwoCallsThrow()));

        session.SubmitPrompt("fail me");
        await WaitForAsync(() => session.State.LastError is { Length: > 0 });
        await WaitForAsync(() => !session.State.IsRunning);
        await Assert.That(session.State.LastError).IsNotNull();

        session.SubmitPrompt("succeed now");
        // The new run clears the stale error immediately on start.
        await WaitForAsync(() => session.State.LastError is null);
        await WaitForAsync(() => !session.State.IsRunning);
        await Assert.That(session.State.LastError).IsNull();
        await Assert.That(session.State.Messages.OfType<AssistantMessage>()
            .Any(m => m.Text == "ok")).IsTrue();
    }

    [Test]
    public async Task Resume_WithConfig_LoadsTranscriptIntoState()
    {
        var stored = CodingSession.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "from disk" });

        var session = _factory.Resume(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))), stored.Id);

        await Assert.That(session.State.SessionId).IsEqualTo(stored.Id);
        await Assert.That(session.State.Messages.Count).IsEqualTo(1);
        await Assert.That(
            ((UserMessage)session.State.Messages[0]).Text).IsEqualTo("from disk");
        await Assert.That(session.IsPersisted).IsTrue();
    }

    // ──────────────────── Cumulative stats ────────────────────

    [Test]
    public async Task SubmitTurn_PopulatesCumulativeStats()
    {
        var usage = new Usage
        {
            Input = 50,
            Output = 20,
            CacheRead = 5,
            CacheWrite = 2,
            TotalTokens = 77,
        };
        var turnEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("hi"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("hi")],
                Usage = usage,
                StopReason = StopReasons.Stop,
            }),
        };
        var session = _factory.Create(
            ConfigWith(StubProvider.Echo(turnEvents)));

        session.SubmitPrompt("hello");
        await WaitForAsync(() => !session.State.IsRunning);

        await Assert.That(session.State.Stats.TurnCount).IsEqualTo(1);
        await Assert.That(session.State.Stats.InputTokens).IsEqualTo(50 + 5 + 2);
        await Assert.That(session.State.Stats.OutputTokens).IsEqualTo(20);
        await Assert.That(session.State.Stats.TotalTokens).IsEqualTo(77);
    }

    [Test]
    public async Task Resume_RecoversCumulativeStatsFromHistory()
    {
        // Handcraft a stored session with non-zero usage + a tool call so
        // the calculator has something to aggregate on reload.
        var stored = CodingSession.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "first prompt" });
        stored.AppendMessage(new AssistantMessage
        {
            Content = [
                new TextBlock("answer"),
                new ToolCall("t1", "read") { Arguments = [] },
            ],
            Usage = new Usage
            {
                Input = 100,
                Output = 40,
                CacheRead = 10,
                CacheWrite = 0,
                TotalTokens = 150,
            },
            StopReason = StopReasons.ToolUse,
        });
        stored.AppendMessage(new ToolResultMessage
        {
            ToolCallId = "t1",
            ToolName = "read",
            Content = [new TextBlock("file body")],
            IsError = false,
        });
        stored.AppendMessage(new AssistantMessage
        {
            Content = [new TextBlock("done")],
            Usage = new Usage
            {
                Input = 30,
                Output = 15,
                TotalTokens = 45,
            },
            StopReason = StopReasons.Stop,
        });
        var storedId = stored.Id;

        // Resume via the runtime factory — this is what the TUI does on
        // popup resume or `phi --session <id>`.
        var resumed = _factory.Resume(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))), storedId);

        await Assert.That(resumed.State.SessionId).IsEqualTo(storedId);
        await Assert.That(resumed.State.Messages.Count).IsEqualTo(4);
        var assistantMsgs = resumed.State.Messages.OfType<AssistantMessage>().ToList();
        await Assert.That(assistantMsgs.Count).IsEqualTo(2);
        await Assert.That(assistantMsgs[0].Usage.Input).IsEqualTo(100);
        await Assert.That(assistantMsgs[1].Usage.Input).IsEqualTo(30);
        await Assert.That(resumed.State.Stats.TurnCount).IsEqualTo(1);
        await Assert.That(resumed.State.Stats.ToolCallCount).IsEqualTo(1);
        await Assert.That(resumed.State.Stats.InputTokens).IsEqualTo(100 + 10 + 30);
        await Assert.That(resumed.State.Stats.OutputTokens).IsEqualTo(40 + 15);
        await Assert.That(resumed.State.Stats.TotalTokens).IsEqualTo(150 + 45);
    }

    [Test]
    public async Task ResumeViaResumeSession_RecoversCumulativeStats()
    {
        // In-place resume (popup flow) must also surface the loaded stats.
        var stored = CodingSession.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "hi" });
        stored.AppendMessage(new AssistantMessage
        {
            Content = [new TextBlock("hello")],
            Usage = new Usage { Input = 10, Output = 5, TotalTokens = 15 },
            StopReason = StopReasons.Stop,
        });

        var live = _factory.Create(
            ConfigWith(StubProvider.Echo(StubProvider.TextTurn("ok"))));

        await ((ISession)live).ResumeSession(stored.Id);

        await Assert.That(live.State.Stats.TurnCount).IsEqualTo(1);
        await Assert.That(live.State.Stats.InputTokens).IsEqualTo(10);
        await Assert.That(live.State.Stats.OutputTokens).IsEqualTo(5);
    }

    [Test]
    public async Task Dispose_WhileRunning_CancelsRunAndFlipsIsRunningFalse()
    {
        // Simulates TUI exit (Ctrl+Q) while a model call is in flight:
        // Dispose must cancel the in-flight run so the LLM doesn't keep
        // burning tokens after the user quits.
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var session = _factory.Create(
            ConfigWith(StubProvider.FirstCallBlocks(gate, "unblocked")));

        session.SubmitPrompt("long running");
        await WaitForAsync(() => session.State.IsRunning);

        session.Dispose();

        await WaitForAsync(() => !session.State.IsRunning);
        await Assert.That(session.State.IsRunning).IsFalse();

        // Disposing twice is a no-op
        session.Dispose();
        await Assert.That(session.State.IsRunning).IsFalse();
    }
}
