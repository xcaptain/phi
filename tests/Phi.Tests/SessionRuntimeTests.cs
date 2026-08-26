using Phi.Agent;
using Phi.Resources;
using Phi.Tests.Helpers;

namespace Phi.Tests;

/// <summary>
/// Runtime behavior of <see cref="Session"/>: lazy persistence
/// (a fresh session writes nothing until its first message), provider
/// injection, and per-message durability during a run. Session switching
/// (new / resume) is tested in <see cref="SessionSwitchTests"/>.
/// </summary>
[NotInParallel("session-tests")]
public class SessionRuntimeTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;
    private readonly string _previousPhiHome;

    public SessionRuntimeTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), "phi-runtime-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
        _phiHome = Path.Combine(Path.GetTempPath(), "phi-home-" + Guid.NewGuid().ToString("N"));
        _previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = _phiHome;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SessionPaths.PhiHome = _previousPhiHome;
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_phiHome)) Directory.Delete(_phiHome, recursive: true);
    }

    private Task<Session> Create(IPhiProvider provider, string? cwd = null) =>
        TestSessionFactory.CreateAsync(cwd ?? _cwd, provider);

    private Task<Session> Resume(IPhiProvider provider, string id) =>
        TestSessionFactory.ResumeAsync(_cwd, provider, id);

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
        var session = Session.Create(_cwd, "m");

        await Assert.That(session.IsPersisted).IsFalse();
        await Assert.That(File.Exists(SessionPaths.IndexFileFor(_cwd))).IsFalse();
        await Assert.That(
            File.Exists(SessionPaths.SessionFileFor(_cwd, session.Id))).IsFalse();
        await Assert.That(new SessionManager(_cwd).ListSessions()).IsEmpty();
    }

    [Test]
    public async Task AppendMessage_FirstCall_PersistsIndexAndTranscript()
    {
        var session = Session.Create(_cwd, "m");

        session.AppendMessage(new UserMessage { Content = "hi" });

        await Assert.That(session.IsPersisted).IsTrue();
        var manager = new SessionManager(_cwd);
        await Assert.That(manager.FindSession(session.Id)).IsNotNull();
        await Assert.That(File.Exists(manager.SessionFileFor(session.Id))).IsTrue();
    }

    [Test]
    public async Task Rename_UnpersistedSession_PersistsWithTitle()
    {
        var session = Session.Create(_cwd, "m");

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
        var session = await Create(provider);

        session.SubmitPrompt("ping");

        await WaitForAsync(() =>
            session.State.Messages.OfType<AssistantMessage>().Any(m => m.Text == "pong"));
        await Assert.That(provider.CallCount).IsGreaterThan(0);
    }

    [Test]
    public async Task StartRuntime_StateCarriesIdentityAndPersistenceFlag()
    {
        var session = await Create(StubProvider.Echo(StubProvider.TextTurn("x")));

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
        var session = await Create(StubProvider.FirstCallBlocks(gate));

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
        var session = await Create(StubProvider.Echo(StubProvider.TextTurn("done")));

        session.SubmitPrompt("go");

        await WaitForAsync(() =>
            session.State.Messages.OfType<AssistantMessage>().Any(m => m.Text == "done"));
        await WaitForAsync(() => !session.State.IsRunning);

        var loaded = Session.Resume(session.Id, _cwd).LoadMessages();
        await Assert.That(loaded.OfType<UserMessage>().Any(m => m.Text == "go")).IsTrue();
        await Assert.That(
            loaded.OfType<AssistantMessage>().Any(m => m.Text == "done")).IsTrue();
    }

    // ──────────────────── Navigation ────────────────────
    // Session switching (new / resume) is owned by SessionNavigator — see
    // SessionNavigatorTests. These runtime tests cover the factory-level
    // resume path only.

    [Test]
    public async Task LoadSkill_SubmitsSkillAsPrompt_AndRunsTurn()
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

        var config = StubProvider.Echo(StubProvider.TextTurn("ok"));
        var session = await Create(config, projectRoot);

        // Returns the content that was submitted as the user prompt.
        var content = await ((ISession)session).LoadSkillAsync("dotnet-testing");

        await Assert.That(content).Contains("Test the dotnet code with xUnit.");
        // The block anchors the skill's directory so the model can resolve
        // relative references (references/, scripts/) to absolute paths.
        await Assert.That(content).Contains(skillDir);
        await Assert.That(content).Contains("dotnet-testing");
        // pi-style <skill> block: the frontmatter is stripped, the body is
        // trimmed, and the whole message round-trips through the parser.
        await Assert.That(content).DoesNotContain("description: Write xUnit tests");
        await Assert.That(content).StartsWith("<skill name=\"dotnet-testing\"");
        await Assert.That(content).EndsWith("</skill>");
        await Assert.That(SkillInvocation.TryParse(content, out var parsed)).IsTrue();
        await Assert.That(parsed!.Content).Contains("Test the dotnet code with xUnit.");

        // A bare /skill:name triggers a turn: the skill content becomes a
        // user message and the model replies (previously nothing ran).
        await WaitForAsync(() =>
            session.State.Messages.OfType<AssistantMessage>().Any(m => m.Text == "ok"));
        await Assert.That(session.State.Messages.OfType<UserMessage>()
            .Any(u => u.Text.Contains("xUnit"))).IsTrue();

        // Persisted: the message survives a reload from disk.
        var reloaded = Session.Resume(session.Id, projectRoot).LoadMessages();
        await Assert.That(reloaded.OfType<UserMessage>().Any(u => u.Text.Contains("xUnit"))).IsTrue();
    }

    [Test]
    public async Task LoadSkill_WithPrompt_FusesPromptIntoTheSkillMessage()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(_cwd, "..", "proj-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(projectRoot, ".git"));
        var skillDir = Path.GetFullPath(Path.Combine(projectRoot, ".agents", "skills", "dotnet-testing"));
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
            "---\nname: dotnet-testing\ndescription: Write xUnit tests\n---\nWrite xUnit tests.\n");

        var config = StubProvider.Echo(StubProvider.TextTurn("ok"));
        var session = await Create(config, projectRoot);

        var content = await ((ISession)session).LoadSkillAsync("dotnet-testing", "translate to spanish");

        await Assert.That(content).Contains("Write xUnit tests.");
        await Assert.That(content).Contains("translate to spanish");
        await WaitForAsync(() => !session.State.IsRunning);

        // One fused user message — the trailing prompt rides inside the same
        // message as the skill body, mirroring pi's /skill:name args behavior.
        var user = session.State.Messages.OfType<UserMessage>().SingleOrDefault(u => u.Text.Contains("Write xUnit tests."));
        await Assert.That(user).IsNotNull();
        await Assert.That(user!.Text).Contains("translate to spanish");
    }

    [Test]
    public async Task LoadSkill_UnknownSkill_Throws()
    {
        var session = await Create(StubProvider.Echo(StubProvider.TextTurn("ok")));

        var ex = await Assert.That(async () => await ((ISession)session).LoadSkillAsync("nope"))
            .Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("nope");
    }

    [Test]
    public async Task LoadSkill_WhileRunning_Throws()
    {
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // The auto-namer answers instantly; the real run blocks on the gate
        // so the session stays busy while LoadSkillAsync is called.
        var session = await Create(StubProvider.SecondCallBlocks(gate));

        session.SubmitPrompt("long running");
        await WaitForAsync(() => session.State.IsRunning);

        var ex = await Assert.That(async () => await ((ISession)session).LoadSkillAsync("anything"))
            .Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("in progress");

        session.Cancel();
        await WaitForAsync(() => !session.State.IsRunning);
    }

    [Test]
    public async Task SubmitPrompt_ProviderError_SetsLastErrorWithoutThrowing()
    {
        // A provider-level failure (no response-end event) becomes a terminal
        // error assistant message; the session routes its ErrorMessage into
        // LastError so the status bar can display it, and persists the
        // message into history.
        var session = await Create(StubProvider.Echo(
            new AssistantErrorEvent("HTTP 503: overloaded") { HttpStatus = 503 }));

        session.SubmitPrompt("hi");
        await WaitForAsync(() => session.State.LastError is { Length: > 0 });
        await WaitForAsync(() => !session.State.IsRunning);

        await Assert.That(session.State.LastError).Contains("503");
        var errorMessage = session.State.Messages.OfType<AssistantMessage>().Single();
        await Assert.That(errorMessage.StopReason).IsEqualTo(StopReasons.Error);
        await Assert.That(errorMessage.ErrorMessage).Contains("503");
    }

    [Test]
    public async Task NewRun_ClearsPreviousLastError()
    {
        // First run fails (LastError set); the next run must start with a
        // clean LastError so the status bar can restore its normal display
        // and a fresh failure leaves a new record.
        var session = await Create(StubProvider.FirstTwoCallsThrow());

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
        var stored = Session.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "from disk" });

        var session = await Resume(StubProvider.Echo(StubProvider.TextTurn("ok")), stored.Id);

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
            new TextDeltaEvent("hi"),
            new AssistantDoneEvent(new AssistantMessage
            {
                Content = [new TextBlock("hi")],
                Usage = usage,
                StopReason = StopReasons.Stop,
            }),
        };
        var session = await Create(StubProvider.Echo(turnEvents));

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
        var stored = Session.Create(_cwd, "m");
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
        var resumed = await Resume(StubProvider.Echo(StubProvider.TextTurn("ok")), storedId);

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
    public async Task Dispose_WhileRunning_CancelsRunAndFlipsIsRunningFalse()
    {
        // Simulates TUI exit (Ctrl+Q) while a model call is in flight:
        // Dispose must cancel the in-flight run so the LLM doesn't keep
        // burning tokens after the user quits.
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var session = await Create(StubProvider.FirstCallBlocks(gate, "unblocked"));

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
