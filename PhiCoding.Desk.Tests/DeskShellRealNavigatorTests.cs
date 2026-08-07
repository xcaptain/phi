using Aprillz.MewUI;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Desk.Tests;

/// <summary>
/// End-to-end: a real <see cref="SessionNavigator"/> driving
/// <see cref="DeskShell"/>. Clicking "New Chat" / a session (navigation)
/// must keep the chat page in the view host with its editor laid out.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class DeskShellRealNavigatorTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;

    public DeskShellRealNavigatorTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-desk-shell-{Guid.NewGuid():N}");
        _phiHome = Path.Combine(Path.GetTempPath(), $"phi-desk-shell-home-{Guid.NewGuid():N}");
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

    private (SessionNavigator navigator, ProviderManager providers) CreateNavigator()
    {
        MewTestHost.EnsureBackend();
        var providers = new ProviderManager();
        var factory = new CodingSessionFactory(providers);
        var env = new SessionConfig
        {
            Cwd = _cwd,
            ProviderName = "deepseek",
            Model = "deepseek-v4-flash",
            Tools = [],
        };
        var navigator = new SessionNavigator(factory, env, resumeSessionId: null);
        return (navigator, providers);
    }

    [Test]
    public async Task NavigateToNew_KeepsChatPageWithEditor()
    {
        var (navigator, providers) = CreateNavigator();
        using var shell = new DeskShell(navigator, providers, dispatchToUi: action => action());

        await Assert.That(shell.ChatPage).IsNotNull();

        await navigator.NavigateToNewAsync();

        await Assert.That(shell.ViewHost.Content).IsNotNull();
        var page = shell.ChatPage;
        await Assert.That(page).IsNotNull();
        page!.Root.Measure(new Size(800, 600));
        page.Root.Arrange(new Rect(0, 0, 800, 600));
        await Assert.That(page.PromptInputRoot.RenderSize.Height).IsGreaterThan(0);
    }

    [Test]
    public async Task ResumeSession_KeepsChatPageWithEditor()
    {
        var (navigator, providers) = CreateNavigator();
        using var shell = new DeskShell(navigator, providers, dispatchToUi: action => action());

        // Create + persist a session on disk, then navigate to a new one so
        // resume has a target.
        var current = (CodingSession)navigator.Current;
        current.AppendMessage(new PhiAgent.UserMessage { Content = "hi" });
        var sessionId = current.Id;

        await navigator.NavigateToNewAsync();
        await Assert.That(shell.ChatPage).IsNotNull();

        await navigator.ResumeAsync(sessionId);

        await Assert.That(shell.ViewHost.Content).IsNotNull();
        var page = shell.ChatPage;
        await Assert.That(page).IsNotNull();
        page!.Root.Measure(new Size(800, 600));
        page.Root.Arrange(new Rect(0, 0, 800, 600));
        await Assert.That(page.PromptInputRoot.RenderSize.Height).IsGreaterThan(0);
    }
}