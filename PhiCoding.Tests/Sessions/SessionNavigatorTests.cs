using PhiAgent;
using PhiCoding.Providers;
using PhiCoding.Sessions;
using PhiCoding.Tests.Helpers;
using PhiProvider;

namespace PhiCoding.Tests.Sessions;

/// <summary>
/// <see cref="SessionNavigator"/>: route→session mapping, navigation
/// lifecycle (dispose of the outgoing session, cancel of an in-flight run),
/// fresh-session provider/model carry-over, and the "switching sessions
/// switches the live provider" regression formerly covered by in-place
/// <c>ResumeSession</c>.
/// </summary>
[NotInParallel("session-tests")]
public class SessionNavigatorTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;
    private readonly FakeProviderResolver _resolver = new();
    private readonly CodingSessionFactory _factory;

    public SessionNavigatorTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-nav-{Guid.NewGuid():N}");
        _phiHome = Path.Combine(Path.GetTempPath(), $"phi-nav-home-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("PHI_HOME", _phiHome);
        Directory.CreateDirectory(_cwd);
        _factory = new CodingSessionFactory(_resolver);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PHI_HOME", null);
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_phiHome)) Directory.Delete(_phiHome, recursive: true);
        GC.SuppressFinalize(this);
    }

    private SessionConfig Env(
        IPhiProvider? provider = null, string model = "stub-model", string providerName = "") => new()
        {
            Cwd = _cwd,
            Provider = provider,
            Model = model,
            ProviderName = providerName,
            MaxTurns = 5,
            Tools = [],
        };

    private SessionNavigator CreateNavigator(SessionConfig? env = null, SessionRoute? route = null) =>
        new(_factory, env ?? Env(), route ?? new NewSessionRoute());

    // ──────── Constructor ────────

    [Test]
    public async Task Constructor_WithNewRoute_BuildsFreshUnpersistedSession()
    {
        var navigator = CreateNavigator();

        await Assert.That(navigator.Route).IsEqualTo(new NewSessionRoute());
        var session = (CodingSession)navigator.Current;
        await Assert.That(session.IsPersisted).IsFalse();
        await Assert.That(File.Exists(SessionPaths.IndexFileFor(_cwd))).IsFalse();
    }

    [Test]
    public async Task Constructor_WithExistingRoute_ResumesRecordedSession()
    {
        var stored = CodingSession.Create(_cwd, "record-model");
        stored.AppendMessage(new UserMessage { Content = "on disk" });

        var navigator = CreateNavigator(route: new ExistingSessionRoute(stored.Id));

        await Assert.That(navigator.Route).IsEqualTo(new ExistingSessionRoute(stored.Id));
        var session = (CodingSession)navigator.Current;
        await Assert.That(session.State.SessionId).IsEqualTo(stored.Id);
        await Assert.That(session.State.Messages).Count().IsEqualTo(1);
        await Assert.That(((UserMessage)session.State.Messages[0]).Text).IsEqualTo("on disk");
    }

    [Test]
    public async Task Constructor_WithUnknownExistingRoute_Throws()
    {
        await Assert.That(() => CreateNavigator(route: new ExistingSessionRoute("nope")))
            .Throws<InvalidOperationException>();
    }

    // ──────── Navigation ────────

    [Test]
    public async Task NavigateToExisting_SwapsSessionAndDisposesPrevious()
    {
        var tracked = new TrackedProvider();
        var navigator = CreateNavigator(Env(tracked));
        var target = CodingSession.Create(_cwd, "m", title: "target");
        target.AppendMessage(new UserMessage { Content = "old conversation" });

        await navigator.NavigateAsync(new ExistingSessionRoute(target.Id));

        var current = (CodingSession)navigator.Current;
        await Assert.That(current.State.SessionId).IsEqualTo(target.Id);
        await Assert.That(current.State.SessionTitle).IsEqualTo("target");
        await Assert.That(current.State.Messages).Count().IsEqualTo(1);
        await Assert.That(((UserMessage)current.State.Messages[0]).Text).IsEqualTo("old conversation");
        await Assert.That(tracked.Disposed).IsTrue();
    }

    [Test]
    public async Task NavigateToExisting_NewAppendsLandInTargetFile()
    {
        // The outgoing session keeps its own storage: messages appended
        // after navigation go to the target's file, never the old one.
        var navigator = CreateNavigator();
        var session = (CodingSession)navigator.Current;
        session.AppendMessage(new UserMessage { Content = "mine" });
        var originalId = session.Id;

        var target = CodingSession.Create(_cwd, "m");
        target.AppendMessage(new UserMessage { Content = "theirs" });

        await navigator.NavigateAsync(new ExistingSessionRoute(target.Id));
        var current = (CodingSession)navigator.Current;
        current.AppendMessage(new UserMessage { Content = "after resume" });

        var targetMessages = CodingSession.Resume(target.Id, _cwd).LoadMessages();
        await Assert.That(targetMessages.OfType<UserMessage>().Select(m => m.Text))
            .IsEquivalentTo(["theirs", "after resume"]);

        var originalMessages = CodingSession.Resume(originalId, _cwd).LoadMessages();
        await Assert.That(originalMessages.OfType<UserMessage>().Select(m => m.Text))
            .IsEquivalentTo(["mine"]);
    }

    [Test]
    public async Task NavigateWhileRunning_CancelsRunThenSwitches()
    {
        // Second call (the real harness run) blocks on the gate; navigation
        // must cancel + await it before swapping, and the in-flight prompt
        // must still land in the outgoing session's file.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var navigator = CreateNavigator(Env(StubProvider.SecondCallBlocks(gate, "unblocked")));
        var sessionA = (CodingSession)navigator.Current;
        var originalId = sessionA.Id;

        var sessionB = CodingSession.Create(_cwd, "m");
        sessionB.AppendMessage(new UserMessage { Content = "existing b" });

        sessionA.SubmitPrompt("prompt a");
        await WaitForAsync(() => sessionA.State.IsRunning);

        await navigator.NavigateAsync(new ExistingSessionRoute(sessionB.Id));

        var current = (CodingSession)navigator.Current;
        await Assert.That(current.State.IsRunning).IsFalse();
        await Assert.That(current.State.SessionId).IsEqualTo(sessionB.Id);

        // Prompt A flushed to the old session file before the cancel.
        var manager = new SessionManager(_cwd);
        await Assert.That(File.ReadAllText(manager.SessionFileFor(originalId)).Contains("prompt a")).IsTrue();

        // Session B file untouched by the navigation.
        var bMessages = CodingSession.Resume(sessionB.Id, _cwd).LoadMessages();
        await Assert.That(bMessages.OfType<UserMessage>().Select(m => m.Text))
            .IsEquivalentTo(["existing b"]);
    }

    [Test]
    public async Task NavigateToUnknownId_Throws_LeavesCurrentUntouched()
    {
        var navigator = CreateNavigator();
        var currentId = navigator.Current.State.SessionId;

        await Assert.That(() => navigator.NavigateAsync(new ExistingSessionRoute("does-not-exist")))
            .Throws<InvalidOperationException>();

        await Assert.That(navigator.Current.State.SessionId).IsEqualTo(currentId);
        await Assert.That(navigator.Route).IsEqualTo(new NewSessionRoute());
    }

    [Test]
    public async Task NavigateToNew_StartsFreshEmptyUnpersistedSession()
    {
        var navigator = CreateNavigator(Env(StubProvider.Echo(StubProvider.TextTurn("ok"))));
        var session = (CodingSession)navigator.Current;
        session.SubmitPrompt("first conversation");
        await WaitForAsync(() => !session.State.IsRunning);
        var oldId = session.Id;
        await Assert.That(session.State.Messages).IsNotEmpty();

        await navigator.NavigateAsync(new NewSessionRoute());

        var fresh = (CodingSession)navigator.Current;
        await Assert.That(fresh.Id).IsNotEqualTo(oldId);
        await Assert.That(fresh.State.SessionId).IsEqualTo(fresh.Id);
        await Assert.That(fresh.State.Messages).IsEmpty();
        await Assert.That(fresh.State.IsPersisted).IsFalse();
        // Provider/model carry over: the user stays connected, just a blank
        // conversation.
        await Assert.That(fresh.State.Model).IsEqualTo("stub-model");
        await Assert.That(File.Exists(SessionPaths.SessionFileFor(_cwd, fresh.Id))).IsFalse();
    }

    [Test]
    public async Task NavigateToNew_CarriesOverCurrentProviderAndModel()
    {
        _resolver.Providers["carrier"] = StubProvider.Echo(StubProvider.TextTurn("ok"));
        var navigator = CreateNavigator(
            Env(provider: null, model: "m-a", providerName: "carrier"));
        await Assert.That(navigator.Current.State.ProviderName).IsEqualTo("carrier");

        await navigator.NavigateAsync(new NewSessionRoute());

        var fresh = (CodingSession)navigator.Current;
        await Assert.That(fresh.State.ProviderName).IsEqualTo("carrier");
        await Assert.That(fresh.State.Model).IsEqualTo("m-a");
    }

    [Test]
    public async Task NavigateToNew_CanSubmitPromptAfterwards()
    {
        var navigator = CreateNavigator(Env(StubProvider.Echo(StubProvider.TextTurn("ok"))));
        var session = (CodingSession)navigator.Current;
        session.SubmitPrompt("old");
        await WaitForAsync(() => !session.State.IsRunning);

        await navigator.NavigateAsync(new NewSessionRoute());

        var fresh = (CodingSession)navigator.Current;
        fresh.SubmitPrompt("new");
        await WaitForAsync(() => !fresh.State.IsRunning);

        await Assert.That(fresh.State.Messages.OfType<UserMessage>().Any(u => u.Text == "new")).IsTrue();
        await Assert.That(fresh.State.Messages.OfType<UserMessage>().Any(u => u.Text == "old")).IsFalse();
    }

    [Test]
    public async Task NavigateToExisting_RecoversCumulativeStatsFromHistory()
    {
        // Navigation to an existing session must surface the loaded stats
        // (usage persisted in the transcript) on the new session's state.
        var stored = CodingSession.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "hi" });
        stored.AppendMessage(new AssistantMessage
        {
            Content = [new TextBlock("hello")],
            Usage = new Usage { Input = 10, Output = 5, TotalTokens = 15 },
            StopReason = StopReasons.Stop,
        });

        var navigator = CreateNavigator();
        await navigator.NavigateAsync(new ExistingSessionRoute(stored.Id));

        var current = (CodingSession)navigator.Current;
        await Assert.That(current.State.Stats.TurnCount).IsEqualTo(1);
        await Assert.That(current.State.Stats.InputTokens).IsEqualTo(10);
        await Assert.That(current.State.Stats.OutputTokens).IsEqualTo(5);
    }

    // ──────── Hot switch regression: provider/model follow the record ────────

    [Test]
    public async Task NavigateToExisting_RebuildsProviderAndStateFromRecord()
    {
        // Regression for the reported bug: after a session switch the status
        // bar must show the target session's provider/model, and the live
        // provider must be rebuilt from the target record — not kept as the
        // current session's.
        var providerA = new TrackedProvider();
        var providerB = new TrackedProvider();
        _resolver.Providers["provider-a"] = providerA;
        _resolver.Providers["provider-b"] = providerB;

        var navigator = CreateNavigator(
            Env(provider: null, model: "model-a", providerName: "provider-a"));

        var sessionB = CodingSession.Create(_cwd, "model-b", providerName: "provider-b");
        sessionB.AppendMessage(new UserMessage { Content = "in b" });

        await navigator.NavigateAsync(new ExistingSessionRoute(sessionB.Id));

        var current = (CodingSession)navigator.Current;
        await Assert.That(current.State.SessionId).IsEqualTo(sessionB.Id);
        await Assert.That(current.State.ProviderName).IsEqualTo("provider-b");
        await Assert.That(current.State.Model).IsEqualTo("model-b");
        await Assert.That(_resolver.Lookups).Contains("provider-b");
        // The outgoing session's provider was released.
        await Assert.That(providerA.Disposed).IsTrue();
    }

    [Test]
    public async Task NavigateToExisting_StateCarriesNewProvider()
    {
        // The TUI binds the status bar to StateChanged; the new session's
        // state must carry the recorded provider/model so the bar never
        // freezes on the previous session's.
        _resolver.Providers["provider-a"] = new TrackedProvider();
        _resolver.Providers["provider-b"] = StubProvider.Echo(StubProvider.TextTurn("ok"));

        var navigator = CreateNavigator(
            Env(provider: null, model: "model-a", providerName: "provider-a"));

        var sessionB = CodingSession.Create(_cwd, "model-b", providerName: "provider-b");
        sessionB.AppendMessage(new UserMessage { Content = "b" });

        await navigator.NavigateAsync(new ExistingSessionRoute(sessionB.Id));

        var current = (CodingSession)navigator.Current;
        await Assert.That(current.State.ProviderName).IsEqualTo("provider-b");
        await Assert.That(current.State.Model).IsEqualTo("model-b");
    }

    // ──────── Listing + disposal ────────

    [Test]
    public async Task ListRecentSessions_DelegatesToSessionManager()
    {
        var navigator = CreateNavigator();
        var stored = CodingSession.Create(_cwd, "m");
        stored.AppendMessage(new UserMessage { Content = "x" });

        var listed = navigator.ListRecentSessions(7);

        await Assert.That(listed.Select(r => r.Id)).IsEquivalentTo([stored.Id]);
    }

    [Test]
    public async Task Dispose_DisposesCurrentSession()
    {
        var tracked = new TrackedProvider();
        var navigator = CreateNavigator(Env(tracked));
        await Assert.That(tracked.Disposed).IsFalse();

        navigator.Dispose();

        await Assert.That(tracked.Disposed).IsTrue();
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
