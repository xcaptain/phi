using System.Runtime.CompilerServices;
using PhiAgent;
using PhiCoding.Prompts;

namespace PhiCoding.Tests;

/// <summary>
/// Runtime provider/model switching on <see cref="CodingSession"/>: model
/// switches reuse the provider instance (and its HTTP transport) while
/// provider switches hand ownership to the session and dispose the outgoing
/// provider; both apply to the next run only.
/// </summary>
[NotInParallel("session-tests")]
public class CodingSessionModelSwitchTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;

    public CodingSessionModelSwitchTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), "phi-switch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
        _phiHome = Path.Combine(Path.GetTempPath(), "phi-switch-home-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("PHI_HOME", _phiHome);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Environment.SetEnvironmentVariable("PHI_HOME", null);
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_phiHome)) Directory.Delete(_phiHome, recursive: true);
    }

    private SessionConfig ConfigWith(
        IPhiProvider provider,
        string providerName = "deepseek",
        string model = "model-a") => new()
        {
            Cwd = _cwd,
            Provider = provider,
            ProviderName = providerName,
            Model = model,
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

    [Test]
    public async Task SwitchModel_UpdatesStateAndRecord_WithoutDisposingProvider()
    {
        var provider = new RecordingProvider();
        var session = CodingSession.Create(ConfigWith(provider));

        session.SwitchModel("model-b");

        await Assert.That(session.State.Model).IsEqualTo("model-b");
        await Assert.That(session.Record.Model).IsEqualTo("model-b");
        await Assert.That(provider.Disposed).IsFalse();

        var reloaded = new SessionManager(_cwd).FindSession(session.Id);
        await Assert.That(reloaded!.Model).IsEqualTo("model-b");
    }

    [Test]
    public async Task SwitchModel_NextRun_UsesNewModel_OnSameProvider()
    {
        var provider = new RecordingProvider();
        var session = CodingSession.Create(ConfigWith(provider));

        session.SubmitPrompt("first");
        await WaitForAsync(() =>
            !session.State.IsRunning
            && session.State.Messages.OfType<AssistantMessage>().Any());

        session.SwitchModel("model-b");
        session.SubmitPrompt("second");
        await WaitForAsync(() => !session.State.IsRunning);

        await Assert.That(provider.Models.Last()).IsEqualTo("model-b");
        await Assert.That(provider.Disposed).IsFalse();
    }

    [Test]
    public async Task SwitchProvider_DisposesPrevious_UpdatesStateAndRecord()
    {
        var providerA = new RecordingProvider();
        var session = CodingSession.Create(ConfigWith(providerA, providerName: "deepseek"));

        var providerB = new RecordingProvider();
        session.SwitchProvider(providerB, "glm", "glm-5.1");

        await Assert.That(providerA.Disposed).IsTrue();
        await Assert.That(session.State.Model).IsEqualTo("glm-5.1");
        await Assert.That(session.State.ProviderName).IsEqualTo("glm");
        await Assert.That(session.Record.Model).IsEqualTo("glm-5.1");
        await Assert.That(session.Record.ProviderName).IsEqualTo("glm");

        var reloaded = new SessionManager(_cwd).FindSession(session.Id);
        await Assert.That(reloaded!.ProviderName).IsEqualTo("glm");
        await Assert.That(reloaded!.Model).IsEqualTo("glm-5.1");
    }

    [Test]
    public async Task SwitchProvider_NextRun_UsesNewProvider()
    {
        var providerA = new RecordingProvider();
        var session = CodingSession.Create(ConfigWith(providerA, providerName: "deepseek"));
        var providerB = new RecordingProvider();
        session.SwitchProvider(providerB, "glm", "glm-5.1");

        session.SubmitPrompt("hello");
        await WaitForAsync(() => !session.State.IsRunning);

        await Assert.That(providerB.Models).IsNotEmpty();
        await Assert.That(providerA.Models).IsEmpty();
        await Assert.That(providerA.Disposed).IsTrue();
    }

    [Test]
    public async Task SwitchProvider_SameInstance_DoesNotDispose()
    {
        var provider = new RecordingProvider();
        var session = CodingSession.Create(ConfigWith(provider, providerName: "deepseek"));

        session.SwitchProvider(provider, "deepseek", "deepseek-v4-pro");

        await Assert.That(provider.Disposed).IsFalse();
        await Assert.That(session.State.Model).IsEqualTo("deepseek-v4-pro");
        await Assert.That(session.State.ProviderName).IsEqualTo("deepseek");
    }

    [Test]
    public async Task SwitchModel_DuringRun_InFlightRunKeepsOldModel()
    {
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new RecordingProvider(gate, gateOnCall: 2);
        var session = CodingSession.Create(ConfigWith(provider, model: "model-a"));

        // Run 1 (auto-name + run consume calls 0 and 1), then a blocked run 2.
        session.SubmitPrompt("first");
        await WaitForAsync(() => !session.State.IsRunning && provider.CallCount >= 2);

        session.SubmitPrompt("second");
        await WaitForAsync(() => session.State.IsRunning && provider.CallCount >= 3);

        session.SwitchModel("model-b");
        gate.SetResult();
        await WaitForAsync(() => !session.State.IsRunning);

        // The in-flight run recorded the model at call start: the old one.
        await Assert.That(provider.Models[2]).IsEqualTo("model-a");

        // The next run picks up the new model.
        session.SubmitPrompt("third");
        await WaitForAsync(() => !session.State.IsRunning);
        await Assert.That(provider.Models.Last()).IsEqualTo("model-b");
    }

    /// <summary>
    /// In-memory provider that records the model of every call and answers
    /// with a single text turn. Optionally blocks a chosen call on a gate.
    /// Tracks disposal for ownership assertions.
    /// </summary>
    private sealed class RecordingProvider : IPhiProvider
    {
        private readonly TaskCompletionSource? _gate;
        private readonly int? _gateOnCall;
        private int _calls;

        public RecordingProvider(TaskCompletionSource? gate = null, int? gateOnCall = null)
        {
            _gate = gate;
            _gateOnCall = gateOnCall;
        }

        public List<string> Models { get; } = [];
        public int CallCount => _calls;
        public bool Disposed { get; private set; }

        public async IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
            string model,
            string system,
            IList<IAgentMessage> messages,
            IReadOnlyList<Tool> tools,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var call = _calls++;
            Models.Add(model);
            if (_gate is not null && _gateOnCall is { } g && call == g)
                await _gate.Task.WaitAsync(cancellationToken);

            yield return new ProviderTextDeltaEvent("ok");
            yield return new ProviderResponseEndEvent(new AssistantMessage
            {
                Model = model,
                Content = [new TextBlock("ok")],
                StopReason = StopReasons.Stop,
            });
        }

        public void Dispose() => Disposed = true;
    }
}
