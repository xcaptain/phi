using Avalonia.Controls;
using Avalonia.Threading;
using PhiAgent;
using PhiCoding.Avalonia.Tests.Helpers;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="ShellView"/>: two-pane shell hosting the chat page and
/// exposing navigation through the sessions list. Mirrors the MewUI desk's
/// shell tests so the behavior contract is preserved across the migration.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ShellViewTests
{
    private static (SessionNavigator navigator, ShellView shell) CreateNavigatorShell(string cwd)
    {
        AvaloniaTestHost.EnsureInitialized();

        var stub = new StubProvider((_, _) => Empty());
        var resolver = new MapResolver(stub);
        var providers = new ProviderManager();
        var factory = new CodingSessionFactory(resolver);
        var env = new SessionConfig { Cwd = cwd, ProviderName = "stub", Model = "m", Tools = [] };
        var navigator = new SessionNavigator(factory, env, resumeSessionId: null);
        var shell = new ShellView(
            navigator,
            providers,
            dispatchToUi: a => a(),
            postToUi: a => a());
        return (navigator, shell);
    }

    private static async IAsyncEnumerable<ProviderEvent> Empty()
    {
        await Task.Yield();
        yield break;
    }

    [Test]
    public async Task InitialState_ShowsChat()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            await Assert.That(shell.ChatPage).IsNotNull();
            await Assert.That(shell.ViewHost.Content).IsEqualTo(shell.ChatPage!.Root);
        }
    }

    [Test]
    public async Task NavModel_Rebuild_AfterSessionPersists()
    {
        var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
        using (shell)
        using (navigator)
        {
            var session = navigator.Current;
            await Assert.That(session.State.IsPersisted).IsFalse();

            session.SubmitPrompt("hello");

            // The session persists asynchronously (first message writes the
            // index); the shell should rebuild the nav so the session row
            // appears and gets highlighted.
            await WaitForAsync(() =>
            {
                var entries = NavModel.BuildMainEntries(
                    WorkspaceSessionStore.ListAllSessions(7),
                    NavModel.GroupMode.ByWorkspace);
                return entries.Any(e =>
                    e.Kind == NavModel.Kind.Session
                    && e.SessionId == session.State.SessionId);
            });
        }
    }

    [Test]
    public async Task SwitchGroupMode_OnRealizedList_DoesNotThrow()
    {
        // Regression: reassigning ItemsSource on a live ListBox recycles its
        // containers, which used to invoke the item template with null data
        // and throw NullReferenceException (seen when clicking "By date").
        // Seed enough sessions that the list actually realizes containers.
        var phiHome = Path.Combine(Path.GetTempPath(), $"phi-av-ws-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("PHI_HOME", phiHome);
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var s = CodingSession.Create(Path.GetTempPath(), "m");
                s.AppendMessage(new PhiAgent.UserMessage { Content = $"seeded {i}" });
            }

            var (navigator, shell) = CreateNavigatorShell(Path.GetTempPath());
            using (shell)
            using (navigator)
            {
                var window = new Window
                {
                    Width = 800,
                    Height = 600,
                    Content = shell.Root,
                };
                window.Show();
                // Realize layout so the ListBox creates containers.
                Dispatcher.UIThread.RunJobs();

                shell.GroupMode = NavModel.GroupMode.ByDate;
                Dispatcher.UIThread.RunJobs();

                // ByDate → ByWorkspace also swaps the entries back.
                shell.GroupMode = NavModel.GroupMode.ByWorkspace;
                Dispatcher.UIThread.RunJobs();

                await Assert.That(shell.SessionsList.ItemsSource).IsNotNull();
                window.Close();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHI_HOME", null);
            if (Directory.Exists(phiHome)) Directory.Delete(phiHome, recursive: true);
        }
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

    /// <summary>Resolves every provider name to a single stub instance.</summary>
    private sealed class MapResolver(IPhiProvider provider) : IProviderResolver
    {
        public IPhiProvider Resolve(string providerName) => provider;
    }
}
