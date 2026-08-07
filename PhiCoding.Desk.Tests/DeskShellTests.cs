using Aprillz.MewUI;
using PhiCoding.Desk.Tests.Helpers;
using PhiCoding.Providers;

namespace PhiCoding.Desk.Tests;

/// <summary>
/// <see cref="DeskShell"/>: clicking "New Chat" or a session item must keep
/// the chat page (with its prompt editor) in the right-side view host.
/// Regression: the editor disappeared after these clicks.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class DeskShellTests
{
    private const double Width = 800;
    private const double Height = 600;

    private static DeskShell CreateShell(MockSession session, out FakeSessionNavigator navigator)
    {
        MewTestHost.EnsureBackend();
        navigator = new FakeSessionNavigator(session);
        return new DeskShell(navigator, new ProviderManager(), dispatchToUi: action => action());
    }

    private static void Layout(DeskChatPage page)
    {
        page.Root.Measure(new Size(Width, Height));
        page.Root.Arrange(new Rect(0, 0, Width, Height));
    }

    [Test]
    public async Task InitialView_IsChatPage()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        await Assert.That(shell.ViewHost.Content).IsNotNull();
        await Assert.That(shell.ChatPage).IsNotNull();
    }

    [Test]
    public async Task ClickNewChat_KeepsChatPageInViewHost()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out var navigator);
        // The chat page starts in the host.
        await Assert.That(shell.ViewHost.Content).IsNotNull();

        shell.Select(DeskNavModel.Kind.NewChat);

        // FakeNavigator fires SessionChanged → OnSessionChanged rebuilds the
        // chat page. The host must still hold a live chat page.
        await Assert.That(shell.ViewHost.Content).IsNotNull();
        await Assert.That(shell.ChatPage).IsNotNull();
        await Assert.That(navigator.NavigateToNewCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ClickNewChat_EditorStillRenders()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        shell.Select(DeskNavModel.Kind.NewChat);

        var page = shell.ChatPage;
        await Assert.That(page).IsNotNull();
        Layout(page!);
        // The prompt editor must have a non-zero height (not collapsed).
        await Assert.That(page!.PromptInputRoot.RenderSize.Height).IsGreaterThan(0);
    }

    [Test]
    public async Task ClickSession_KeepsChatPageInViewHost()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out var navigator);
        // A session entry with a resume id.
        shell.Select(DeskNavModel.Kind.Session, sessionId: "some-session");

        await Assert.That(shell.ViewHost.Content).IsNotNull();
        await Assert.That(shell.ChatPage).IsNotNull();
        await Assert.That(navigator.LastResumedId).IsEqualTo("some-session");
    }

    [Test]
    public async Task ClickModels_ThenClickNewChat_ShowsChatAgain()
    {
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        shell.Select(DeskNavModel.Kind.Models);

        shell.Select(DeskNavModel.Kind.NewChat);
        await Assert.That(shell.ViewHost.Content).IsNotNull();
        await Assert.That(shell.ChatPage).IsNotNull();
    }

    [Test]
    public async Task EditorRendersThroughFullShellTree()
    {
        // Lay out the ENTIRE shell (NavigationView content host → ViewHost →
        // chat page), not just the page. This exercises the same nesting the
        // real window uses; the editor must still have height.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        shell.Select(DeskNavModel.Kind.NewChat);
        var root = shell.BuildRoot();
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));

        var page = shell.ChatPage;
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.PromptInputRoot.RenderSize.Height).IsGreaterThan(0);
        // The transcript area must also get the middle region.
        await Assert.That(page!.TranscriptRoot.RenderSize.Height).IsGreaterThan(100);
    }

    [Test]
    public async Task NavSelectionChange_KeepsEditorRendering()
    {
        // Drive the real NavigationView selection path: setting the nav's
        // SelectedIndex fires SelectionChanged → OnNavSelection → (deferred)
        // HandleSelection → navigation → SessionChanged rebuild. The chat
        // page + editor must still render after the whole chain.
        var session = new MockSession();
        using var shell = CreateShell(session, out _);

        shell.Nav.SelectedIndex = 0;

        var root = shell.BuildRoot();
        root.Measure(new Size(Width, Height));
        root.Arrange(new Rect(0, 0, Width, Height));

        var page = shell.ChatPage;
        await Assert.That(page).IsNotNull();
        await Assert.That(page!.PromptInputRoot.RenderSize.Height).IsGreaterThan(0);
    }
}
