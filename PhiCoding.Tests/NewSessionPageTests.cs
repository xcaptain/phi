using PhiCoding.Tui.Pages;
using PhiCoding.Providers;
using PhiCoding.Tests.Helpers;

namespace PhiCoding.Tests;

/// <summary>
/// <see cref="NewSessionPage"/>: the centered-prompt landing for
/// <c>/sessions/new</c>. Its first-prompt promotion is covered by
/// SessionNavigatorTests (navigating to the current session's own id adopts
/// it without rebuild/cancel).
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class NewSessionPageTests
{
    [Test]
    public async Task Build_SetsEditorAndSuggestionStrip()
    {
        var session = new MockSession();
        var page = new NewSessionPage(
            session, new FakeSessionNavigator(session), new ProviderManager());

        var root = page.Build();

        await Assert.That(root).IsNotNull();
        await Assert.That(page.Input.Editor).IsNotNull();
        await Assert.That(page.Input.SuggestionStrip).IsNotNull();
    }
}
