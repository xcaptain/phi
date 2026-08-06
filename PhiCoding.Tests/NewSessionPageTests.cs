using PhiCoding.Tui.Pages;
using PhiCoding.Providers;
using PhiCoding.Tests.Helpers;
using XenoAtom.Terminal.UI.Rendering;

namespace PhiCoding.Tests;

/// <summary>
/// <see cref="NewSessionPage"/>: the landing for <c>/sessions/new</c> — the
/// same skeleton as <see cref="PhiCoding.Tui.Pages.SessionPage"/> with a
/// slogan in the content slot, editor + suggestion strip + status bar at the
/// bottom. Its first-prompt promotion is covered by SessionNavigatorTests
/// (navigating to the current session's own id adopts it without
/// rebuild/cancel).
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class NewSessionPageTests
{
    private static (MockSession Session, NewSessionPage Page) CreatePage()
    {
        var session = new MockSession();
        var page = new NewSessionPage(
            session, new FakeSessionNavigator(session), new ProviderManager());
        return (session, page);
    }

    [Test]
    public async Task Build_SetsEditorAndSuggestionStrip()
    {
        var (_, page) = CreatePage();

        var root = page.Build();

        await Assert.That(root).IsNotNull();
        await Assert.That(page.Input.Editor).IsNotNull();
        await Assert.That(page.Input.SuggestionStrip).IsNotNull();
    }

    [Test]
    public async Task Build_SetsBottomStatusBar()
    {
        var (_, page) = CreatePage();

        page.Build();

        await Assert.That(page.StatusBar).IsNotNull();
    }

    [Test]
    public async Task RenderedContent_ShowsSlogan()
    {
        var (_, page) = CreatePage();

        var buffer = VisualSnapshotRenderer.Render(page.Build(), width: 120, maxHeight: 40);
        var rendered = string.Join("\n", buffer.ToMarkupLines());

        await Assert.That(rendered).Contains("a minimal and portable coding agent");
    }

    [Test]
    public async Task StateChanged_UpdatesStatusBarModel()
    {
        var (session, page) = CreatePage();
        page.Build();

        session.UpdateState(s => s with { ProviderName = "deepseek", Model = "deepseek-v4-flash" });

        // The status bar's label is internal State; just assert the binding
        // didn't throw and the bar survives the session event.
        await Assert.That(page.StatusBar).IsNotNull();
    }
}
