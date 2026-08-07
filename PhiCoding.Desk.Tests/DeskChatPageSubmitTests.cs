using PhiAgent;
using PhiCoding.Desk.Tests.Helpers;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Desk.Tests;

/// <summary>
/// End-to-end submit flow on a real <see cref="CodingSession"/>: the prompt
/// input writes the user's message into the projector AND submits to the
/// session; after the turn the transcript shows the user bubble + the
/// assistant response.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class DeskChatPageSubmitTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;

    public DeskChatPageSubmitTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-desk-submit-{Guid.NewGuid():N}");
        _phiHome = Path.Combine(Path.GetTempPath(), $"phi-desk-submit-home-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("PHI_HOME", _phiHome);
        Directory.CreateDirectory(_cwd);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PHI_HOME", null);
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_phiHome)) Directory.Delete(_phiHome, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 8000)
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
    public async Task SubmitPrompt_RendersUserBubbleAndAssistantResponse()
    {
        MewTestHost.EnsureBackend();

        // A provider that responds "hi there" to every call (the first call
        // is the session auto-namer, so it answers too).
        var provider = StubProvider.Echo(StubProvider.TextTurn("hi there"));
        var resolver = new MapResolver(provider);
        var factory = new CodingSessionFactory(resolver);
        var env = new SessionConfig { Cwd = _cwd, ProviderName = "stub", Model = "m", Tools = [] };
        var navigator = new SessionNavigator(factory, env, resumeSessionId: null);

        using (var page = new DeskChatPage(navigator, new ProviderManager(), navigator.Current))
        {
            // The user bubble appears immediately on submit.
            page.PromptInput.Text.Value = "hello";
            page.PromptInput.SubmitForTest();
            await Assert.That(page.Transcript.LineCount).IsGreaterThanOrEqualTo(1);

            // Wait for the turn to complete.
            await WaitForAsync(() => !navigator.Current.State.IsRunning);

            // User bubble + assistant response.
            await Assert.That(page.Transcript.LineCount).IsGreaterThanOrEqualTo(2);
        }

        navigator.Dispose();
    }

    /// <summary>Resolves every provider name to a single stub instance.</summary>
    private sealed class MapResolver(IPhiProvider provider) : IProviderResolver
    {
        public IPhiProvider Resolve(string providerName) => provider;
    }
}
