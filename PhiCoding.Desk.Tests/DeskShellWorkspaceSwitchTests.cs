using PhiAgent;
using PhiCoding.Desk.Tests.Helpers;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Desk.Tests;

/// <summary>
/// Reproduces the "picked a folder in New Chat, then can't send" report:
/// after selecting a workspace the fresh session is recreated in that folder,
/// and submitting from the NEW editor must still reach the session.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class DeskShellWorkspaceSwitchTests : IDisposable
{
    private readonly string _cwdA;
    private readonly string _cwdB;
    private readonly string _phiHome;

    public DeskShellWorkspaceSwitchTests()
    {
        _cwdA = Path.Combine(Path.GetTempPath(), $"phi-desk-ws-a-{Guid.NewGuid():N}");
        _cwdB = Path.Combine(Path.GetTempPath(), $"phi-desk-ws-b-{Guid.NewGuid():N}");
        _phiHome = Path.Combine(Path.GetTempPath(), $"phi-desk-ws-home-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("PHI_HOME", _phiHome);
        Directory.CreateDirectory(_cwdA);
        Directory.CreateDirectory(_cwdB);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PHI_HOME", null);
        foreach (var dir in new[] { _cwdA, _cwdB, _phiHome })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
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

    /// <summary>
    /// Waits until the nav highlight moves off "New Chat" onto the session.
    /// The auto-namer and the run settle asynchronously, so the rebuild and
    /// selection can land after the state flags flip.
    /// </summary>
    private static async Task WaitForNavHighlightAsync(DeskShell shell, ISession session, int timeoutMs = 8000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var entries = DeskNavModel.BuildMainEntries(WorkspaceSessionStore.ListAllSessions());
            var expected = DeskNavModel.IndexForActive(entries, session.State.SessionId);
            if (expected > 0 && shell.Nav.SelectedIndex == expected)
                return;
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException(
                    $"Nav did not highlight session {session.State.SessionId} (index {shell.Nav.SelectedIndex}, expected {expected})");
            await Task.Delay(20);
        }
    }

    [Test]
    public async Task SelectWorkspace_ThenSubmit_SendsToNewSession()
    {
        MewTestHost.EnsureBackend();

        var stub = StubProvider.Echo(StubProvider.TextTurn("hi there"));
        var resolver = new MapResolver(stub);
        var providers = new ProviderManager();
        var factory = new CodingSessionFactory(resolver);
        var env = new SessionConfig { Cwd = _cwdA, ProviderName = "stub", Model = "m", Tools = [] };
        var navigator = new SessionNavigator(factory, env, resumeSessionId: null);

        using (var shell = new DeskShell(
                   navigator, providers,
                   dispatchToUi: action => action(),
                   postToUi: action => action()))
        {
            // Fresh session in workspace A.
            var page = shell.ChatPage!;
            await Assert.That(page.PromptInput.Session.Cwd).IsEqualTo(_cwdA);

            // Pick workspace B — recreates the fresh session there.
            page.PromptInput.SelectWorkspaceForTest(_cwdB);

            var newPage = shell.ChatPage!;
            await Assert.That(newPage.PromptInput.Session.Cwd).IsEqualTo(_cwdB);

            // Type + submit from the NEW editor.
            newPage.PromptInput.Text.Value = "hello";
            newPage.PromptInput.SubmitForTest();
            await Assert.That(newPage.Transcript.LineCount).IsGreaterThanOrEqualTo(1);

            await WaitForAsync(() => !navigator.Current.State.IsRunning);
            await Assert.That(newPage.Transcript.LineCount).IsGreaterThanOrEqualTo(2);
        }

        navigator.Dispose();
    }

    [Test]
    public async Task ComboBoxSelectWorkspace_ThenSubmit_SendsToNewSession()
    {
        MewTestHost.EnsureBackend();

        // Pre-seed a session in workspace B so the picker lists both.
        var seedB = CodingSession.Create(_cwdB, "m");
        seedB.AppendMessage(new PhiAgent.UserMessage { Content = "seeded" });

        var stub = StubProvider.Echo(StubProvider.TextTurn("hi there"));
        var resolver = new MapResolver(stub);
        var providers = new ProviderManager();
        var factory = new CodingSessionFactory(resolver);
        var env = new SessionConfig { Cwd = _cwdA, ProviderName = "stub", Model = "m", Tools = [] };
        var navigator = new SessionNavigator(factory, env, resumeSessionId: null);

        using (var shell = new DeskShell(
                   navigator, providers,
                   dispatchToUi: action => action(),
                   postToUi: action => action()))
        {
            var page = shell.ChatPage!;
            await Assert.That(page.PromptInput.Session.Cwd).IsEqualTo(_cwdA);

            var combo = page.PromptInput.WorkspaceComboBox;
            await Assert.That(combo).IsNotNull();
            // Workspace list = [cwdA (current), cwdB]; pick cwdB (index 1).
            combo!.SelectedIndex = 1;

            var newPage = shell.ChatPage!;
            await Assert.That(newPage.PromptInput.Session.Cwd).IsEqualTo(_cwdB);

            newPage.PromptInput.Text.Value = "hello";
            newPage.PromptInput.SubmitForTest();
            await Assert.That(newPage.Transcript.LineCount).IsGreaterThanOrEqualTo(1);

            await WaitForAsync(() => !navigator.Current.State.IsRunning);
            await Assert.That(newPage.Transcript.LineCount).IsGreaterThanOrEqualTo(2);
        }

        navigator.Dispose();
    }

    [Test]
    public async Task FirstMessage_PersistsSession_NavHighlightsTheNewSession()
    {
        MewTestHost.EnsureBackend();

        var stub = StubProvider.Echo(StubProvider.TextTurn("hi there"));
        var resolver = new MapResolver(stub);
        var providers = new ProviderManager();
        var factory = new CodingSessionFactory(resolver);
        var env = new SessionConfig { Cwd = _cwdA, ProviderName = "stub", Model = "m", Tools = [] };
        var navigator = new SessionNavigator(factory, env, resumeSessionId: null);

        using (var shell = new DeskShell(
                   navigator, providers,
                   dispatchToUi: action => action(),
                   postToUi: action => action()))
        {
            var page = shell.ChatPage!;
            await Assert.That(navigator.Current.State.IsPersisted).IsFalse();

            // Send the first message → the session persists to disk.
            page.PromptInput.Text.Value = "hello";
            page.PromptInput.SubmitForTest();
            await WaitForAsync(() => navigator.Current.State.IsPersisted);

            // The nav must now include the new session and highlight it
            // (not stay on "New Chat" at index 0).
            await WaitForNavHighlightAsync(shell, navigator.Current);
        }

        navigator.Dispose();
    }

    [Test]
    public async Task AutoNamer_UpdatesSessionTitle_InNav()
    {
        MewTestHost.EnsureBackend();

        // The stub answers "hi there" to every call, including the session
        // auto-namer, so after the first message the title becomes "hi there".
        var stub = StubProvider.Echo(StubProvider.TextTurn("hi there"));
        var resolver = new MapResolver(stub);
        var providers = new ProviderManager();
        var factory = new CodingSessionFactory(resolver);
        var env = new SessionConfig { Cwd = _cwdA, ProviderName = "stub", Model = "m", Tools = [] };
        var navigator = new SessionNavigator(factory, env, resumeSessionId: null);

        using (var shell = new DeskShell(
                   navigator, providers,
                   dispatchToUi: action => action(),
                   postToUi: action => action()))
        {
            var page = shell.ChatPage!;
            page.PromptInput.Text.Value = "hello";
            page.PromptInput.SubmitForTest();

            // Wait for the auto-namer to produce a title, and for the nav to
            // settle on the session's row.
            await WaitForAsync(() => !string.IsNullOrEmpty(navigator.Current.State.SessionTitle));
            await WaitForNavHighlightAsync(shell, navigator.Current);

            // The nav entry must show the real title, not the id-prefix
            // placeholder.
            var entries = DeskNavModel.BuildMainEntries(WorkspaceSessionStore.ListAllSessions());
            var sessionEntry = entries.Single(e =>
                e.Kind == DeskNavModel.Kind.Session
                && e.SessionId == navigator.Current.State.SessionId);
            await Assert.That(sessionEntry.Title).IsEqualTo("hi there");
        }

        navigator.Dispose();
    }

    /// <summary>Resolves every provider name to a single stub instance.</summary>
    private sealed class MapResolver(IPhiProvider provider) : IProviderResolver
    {
        public IPhiProvider Resolve(string providerName) => provider;
    }
}
