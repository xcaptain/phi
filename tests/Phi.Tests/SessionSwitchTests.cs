using Phi.Agent;
using Phi.Providers;
using Phi.Tests.Helpers;
using Phi.Provider;

namespace Phi.Tests;

/// <summary>
/// Session <em>switching</em> (new / resume) on <see cref="Session"/>:
/// navigation lifecycle (dispose of the outgoing session, cancel of an
/// in-flight run), fresh-session provider/model carry-over, the
/// resolver-driven resume (rebuilds the live provider from the session
/// record's stored name), the cross-workspace resume (the session's
/// own cwd wins), and the record-model-always-wins rule.
/// <para>
/// These tests cover the behaviors that previously lived behind
/// <c>SessionFactory</c> and <c>SessionNavigator</c>. Both classes are
/// gone; navigation is now a method on the session itself, and the
/// composition path is <c>Session.LoadAsync(env, ...)</c> via
/// <see cref="TestSessionFactory"/>.
/// </para>
/// </summary>
[NotInParallel("session-tests")]
public class SessionSwitchTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _cwdB;
    private readonly string _phiHome;
    private readonly string _previousPhiHome;
    private readonly FakeProviderResolver _resolver = new();

    public SessionSwitchTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-switch-{Guid.NewGuid():N}");
        _cwdB = Path.Combine(Path.GetTempPath(), $"phi-switch-b-{Guid.NewGuid():N}");
        _phiHome = Path.Combine(Path.GetTempPath(), $"phi-switch-home-{Guid.NewGuid():N}");
        _previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = _phiHome;
        Directory.CreateDirectory(_cwd);
        Directory.CreateDirectory(_cwdB);
    }

    public void Dispose()
    {
        SessionPaths.PhiHome = _previousPhiHome;
        foreach (var dir in new[] { _cwd, _cwdB, _phiHome })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private async Task<Session> NewSessionAsync(IPhiProvider? provider = null,
        string model = "stub-model", string providerName = "",
        string? cwd = null)
    {
        // When the test passes a provider, wire it into the resolver
        // under the test's providerName so the session picks it up; when
        // no provider is given, fall back to the test's FakeProviderResolver
        // (which can be seeded with name→provider mappings).
        var resolver = provider is not null
            ? (IProviderResolver)new SingleProviderResolver(provider)
            : _resolver;
        var env = new SessionEnvironment
        {
            ProviderResolver = resolver,
            SystemPrompt = new Phi.Prompts.SystemPromptOptions { ResolvedSystemPrompt = "test" },
            MaxTurns = 5,
            ContextWindowTokens = ContextWindow.DefaultContextWindowTokens,
            AutoCompactTokenThreshold = null,
            AutoCompactEnabled = true,
            CompactionKeepRecentTokens = ContextWindow.DefaultCompactionKeepRecentTokens,
            Tools = [],
        };
        return await Session.LoadAsync(cwd ?? _cwd, env, providerName, model);
    }

    private sealed class SingleProviderResolver : IProviderResolver
    {
        private readonly IPhiProvider _provider;
        public SingleProviderResolver(IPhiProvider provider) { _provider = provider; }
        public IPhiProvider Resolve(string providerName) => _provider;
    }

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

    // ──────── Navigation (ISession.NewSessionAsync / ResumeAsync) ────────

    [Test]
    public async Task Resume_SwapsSessionAndDisposesPrevious()
    {
        var tracked = new TrackedProvider();
        var session = await NewSessionAsync(provider: tracked);
        var target = Session.Create(_cwd, "m", title: "target");
        target.AppendMessage(new UserMessage { Content = "old conversation" });

        var next = await session.ResumeAsync(target.Id);

        await Assert.That(next.State.SessionId).IsEqualTo(target.Id);
        await Assert.That(next.State.SessionTitle).IsEqualTo("target");
        await Assert.That(next.State.Messages).Count().IsEqualTo(1);
        await Assert.That(((UserMessage)next.State.Messages[0]).Text).IsEqualTo("old conversation");
        await Assert.That(tracked.Disposed).IsTrue();
    }

    [Test]
    public async Task Resume_NewAppendsLandInTargetFile()
    {
        var session = await NewSessionAsync();
        var originalId = session.Id;
        ((Session)session).AppendMessage(new UserMessage { Content = "mine" });

        var target = Session.Create(_cwd, "m");
        target.AppendMessage(new UserMessage { Content = "theirs" });

        var next = await session.ResumeAsync(target.Id);
        ((Session)next).AppendMessage(new UserMessage { Content = "after resume" });

        var targetMessages = Session.Resume(target.Id, _cwd).LoadMessages();
        await Assert.That(targetMessages.OfType<UserMessage>().Select(m => m.Text))
            .IsEquivalentTo(["theirs", "after resume"]);

        var originalMessages = Session.Resume(originalId, _cwd).LoadMessages();
        await Assert.That(originalMessages.OfType<UserMessage>().Select(m => m.Text))
            .IsEquivalentTo(["mine"]);
    }

    [Test]
    public async Task NavigateWhileRunning_CancelsRunThenSwitches()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionA = await NewSessionAsync(provider: StubProvider.SecondCallBlocks(gate, "unblocked"));
        var originalId = sessionA.Id;

        var sessionB = Session.Create(_cwd, "m");
        sessionB.AppendMessage(new UserMessage { Content = "existing b" });

        sessionA.SubmitPrompt("prompt a");
        await WaitForAsync(() => sessionA.State.IsRunning);

        var sessionC = await sessionA.ResumeAsync(sessionB.Id);

        await Assert.That(sessionC.State.IsRunning).IsFalse();
        await Assert.That(sessionC.State.SessionId).IsEqualTo(sessionB.Id);

        var manager = new SessionManager(_cwd);
        await Assert.That(File.ReadAllText(manager.SessionFileFor(originalId)).Contains("prompt a")).IsTrue();

        var bMessages = Session.Resume(sessionB.Id, _cwd).LoadMessages();
        await Assert.That(bMessages.OfType<UserMessage>().Select(m => m.Text))
            .IsEquivalentTo(["existing b"]);
    }

    [Test]
    public async Task ResumeAsync_UnknownId_Throws_LeavesCurrentUntouched()
    {
        var session = await NewSessionAsync();
        var currentId = session.State.SessionId;

        await Assert.That(() => session.ResumeAsync("does-not-exist"))
            .Throws<InvalidOperationException>();

        // The session must not dispose itself when ResumeAsync throws —
        // the caller has a chance to retry with a different id.
        await Assert.That(session.State.SessionId).IsEqualTo(currentId);
    }

    [Test]
    public async Task NewSessionAsync_StartsFreshEmptyUnpersistedSession()
    {
        var session = await NewSessionAsync(provider: StubProvider.Echo(StubProvider.TextTurn("ok")));
        session.SubmitPrompt("first conversation");
        await WaitForAsync(() => !session.State.IsRunning);
        var oldId = session.Id;
        await Assert.That(session.State.Messages).IsNotEmpty();

        var fresh = (Session)await session.NewSessionAsync();

        await Assert.That(fresh.Id).IsNotEqualTo(oldId);
        await Assert.That(fresh.State.SessionId).IsEqualTo(fresh.Id);
        await Assert.That(fresh.State.Messages).IsEmpty();
        await Assert.That(fresh.State.IsPersisted).IsFalse();
        await Assert.That(fresh.State.Model).IsEqualTo("stub-model");
        await Assert.That(File.Exists(SessionPaths.SessionFileFor(_cwd, fresh.Id))).IsFalse();
    }

    [Test]
    public async Task NewSessionAsync_CarriesOverCurrentProviderAndModel()
    {
        _resolver.Providers["carrier"] = StubProvider.Echo(StubProvider.TextTurn("ok"));
        var session = await NewSessionAsync(model: "m-a", providerName: "carrier");
        await Assert.That(session.State.ProviderName).IsEqualTo("carrier");

        var fresh = (Session)await session.NewSessionAsync();

        await Assert.That(fresh.State.ProviderName).IsEqualTo("carrier");
        await Assert.That(fresh.State.Model).IsEqualTo("m-a");
    }

    [Test]
    public async Task NewSessionAsync_CanSubmitPromptAfterwards()
    {
        var session = await NewSessionAsync(provider: StubProvider.Echo(StubProvider.TextTurn("ok")));
        session.SubmitPrompt("old");
        await WaitForAsync(() => !session.State.IsRunning);

        var fresh = (Session)await session.NewSessionAsync();
        fresh.SubmitPrompt("new");
        await WaitForAsync(() => !fresh.State.IsRunning);

        await Assert.That(fresh.State.Messages.OfType<UserMessage>().Any(u => u.Text == "new")).IsTrue();
        await Assert.That(fresh.State.Messages.OfType<UserMessage>().Any(u => u.Text == "old")).IsFalse();
    }

    [Test]
    public async Task NewSessionAsync_WithCwd_CreatesSessionInThatWorkspace()
    {
        var session = await NewSessionAsync();
        var originalId = session.Id;

        var fresh = (Session)await session.NewSessionAsync(_cwdB);

        await Assert.That(fresh.Id).IsNotEqualTo(originalId);
        await Assert.That(fresh.Cwd).IsEqualTo(_cwdB);
    }

    [Test]
    public async Task ResumeAsync_SessionFromOtherWorkspace_ResolvesItsCwd()
    {
        // A session persisted in a DIFFERENT workspace than the live
        // session's cwd must be resumable — ResumeAsync resolves the
        // record's own cwd instead of assuming the current one's.
        var sessionB = Session.Create(_cwdB, "m");
        sessionB.AppendMessage(new UserMessage { Content = "in workspace b" });

        var session = await NewSessionAsync();
        var next = await session.ResumeAsync(sessionB.Id);

        await Assert.That(next.State.SessionId).IsEqualTo(sessionB.Id);
        await Assert.That(next.Cwd).IsEqualTo(_cwdB);
        await Assert.That(next.State.Messages).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Resume_RecoversCumulativeStatsFromHistory()
    {
        var stored = Session.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "hi" });
        stored.AppendMessage(new AssistantMessage
        {
            Content = [new TextBlock("hello")],
            Usage = new Usage { Input = 10, Output = 5, TotalTokens = 15 },
            StopReason = StopReasons.Stop,
        });

        var session = await NewSessionAsync();
        var next = await session.ResumeAsync(stored.Id);

        await Assert.That(next.State.Stats.TurnCount).IsEqualTo(1);
        await Assert.That(next.State.Stats.InputTokens).IsEqualTo(10);
        await Assert.That(next.State.Stats.OutputTokens).IsEqualTo(5);
    }

    [Test]
    public async Task Resume_RebuildsProviderAndStateFromRecord()
    {
        // Regression: after a session switch the status bar must show
        // the target session's provider/model, and the live provider must
        // be rebuilt from the target record — not kept as the current
        // session's.
        var providerA = new TrackedProvider();
        var providerB = new TrackedProvider();
        _resolver.Providers["provider-a"] = providerA;
        _resolver.Providers["provider-b"] = providerB;

        var session = await NewSessionAsync(model: "model-a", providerName: "provider-a");
        var sessionB = Session.Create(_cwd, "model-b", providerName: "provider-b");
        sessionB.AppendMessage(new UserMessage { Content = "in b" });

        var next = await session.ResumeAsync(sessionB.Id);

        await Assert.That(next.State.SessionId).IsEqualTo(sessionB.Id);
        await Assert.That(next.State.ProviderName).IsEqualTo("provider-b");
        await Assert.That(next.State.Model).IsEqualTo("model-b");
        await Assert.That(_resolver.Lookups).Contains("provider-b");
        // The outgoing session's provider was released.
        await Assert.That(providerA.Disposed).IsTrue();
    }

    [Test]
    public async Task Resume_StateCarriesNewProvider()
    {
        _resolver.Providers["provider-a"] = new TrackedProvider();
        _resolver.Providers["provider-b"] = StubProvider.Echo(StubProvider.TextTurn("ok"));

        var session = await NewSessionAsync(model: "model-a", providerName: "provider-a");
        var sessionB = Session.Create(_cwd, "model-b", providerName: "provider-b");
        sessionB.AppendMessage(new UserMessage { Content = "b" });

        var next = await session.ResumeAsync(sessionB.Id);

        await Assert.That(next.State.ProviderName).IsEqualTo("provider-b");
        await Assert.That(next.State.Model).IsEqualTo("model-b");
    }

    // ──────── Resume semantics (record always wins) ────────

    [Test]
    public async Task Resume_EmptyRecordProviderName_FallsBackToResolver()
    {
        // Legacy record (ProviderName="") is gracefully handled: the
        // resolver sees the empty name and substitutes the catalog default.
        var stored = Session.Create(_cwd, "m"); // ProviderName = ""
        stored.AppendMessage(new UserMessage { Content = "x" });

        var session = await NewSessionAsync(provider: StubProvider.Echo(StubProvider.TextTurn("ok")));
        var next = await session.ResumeAsync(stored.Id);

        await Assert.That(next.State.Model).IsEqualTo("m");
    }

    // ──────── ListRecent ────────

    [Test]
    public async Task ListRecent_DelegatesToSessionManager()
    {
        var session = await NewSessionAsync();
        var stored = Session.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "x" });

        var listed = session.ListRecent(7);

        await Assert.That(listed.Select(r => r.Id)).IsEquivalentTo([stored.Id]);
    }

    // ──────── Disposal ────────

    [Test]
    public async Task Dispose_DisposesProvider()
    {
        var tracked = new TrackedProvider();
        var session = await NewSessionAsync(provider: tracked);
        await Assert.That(tracked.Disposed).IsFalse();

        session.Dispose();

        await Assert.That(tracked.Disposed).IsTrue();
    }

    [Test]
    public async Task NewSessionAsync_DisposesOldSession()
    {
        var tracked = new TrackedProvider();
        var session = await NewSessionAsync(provider: tracked);
        await Assert.That(tracked.Disposed).IsFalse();

        _ = await session.NewSessionAsync();

        await Assert.That(tracked.Disposed).IsTrue();
    }

    [Test]
    public async Task ResumeAsync_DisposesOldSession()
    {
        var tracked = new TrackedProvider();
        var session = await NewSessionAsync(provider: tracked);
        var stored = Session.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "x" });
        await Assert.That(tracked.Disposed).IsFalse();

        _ = await session.ResumeAsync(stored.Id);

        await Assert.That(tracked.Disposed).IsTrue();
    }

    // ──────── AvailableProviders ────────

    [Test]
    public async Task AvailableProviders_ReturnsCatalogNames()
    {
        var session = await NewSessionAsync();

        await Assert.That(session.AvailableProviders).IsNotEmpty();
        // The catalog is environment-specific; just assert it contains at
        // least one of the providers we know about.
        var names = session.AvailableProviders;
        await Assert.That(names.Any(n => n == "openai" || n == "deepseek" || n == "anthropic")).IsTrue();
    }

    // ──────── Test doubles ────────

    /// <summary>Test resolver that records lookups and hands back a provider by name.</summary>
    private sealed class FakeProviderResolver : IProviderResolver
    {
        public Dictionary<string, IPhiProvider> Providers { get; } = [];
        public List<string> Lookups { get; } = [];

        public IPhiProvider Resolve(string providerName)
        {
            Lookups.Add(providerName);
            if (Providers.TryGetValue(providerName, out var p)) return p;
            return new NullProvider();
        }
    }

    /// <summary>Provider that records disposal.</summary>
    private sealed class TrackedProvider : IPhiProvider
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;

        public async IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
            string model,
            string system,
            IList<IAgentMessage> messages,
            IReadOnlyList<Tool> tools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
