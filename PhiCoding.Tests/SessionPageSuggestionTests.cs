using PhiCoding.Providers;
using PhiCoding.Tests.Helpers;
using PhiCoding.Tui.Pages;

namespace PhiCoding.Tests;

/// <summary>
/// Suggestion-strip wiring on <see cref="SessionPage"/> (via its
/// <see cref="PhiCoding.Tui.Components.PromptInput"/>): the live-autocomplete
/// strip is present and uses the slash-command provider.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class SessionPageSuggestionTests
{
    private static SessionPage CreatePage()
    {
        var session = new MockSession();
        var page = new SessionPage(session, new FakeSessionNavigator(session), new ProviderManager());
        page.Build();
        return page;
    }

    [Test]
    public async Task Build_WiresSuggestionStrip_WithSlashProvider()
    {
        var page = CreatePage();

        await Assert.That(page.Input.SuggestionStrip).IsNotNull();
    }

    [Test]
    public async Task WiredStrip_UsesSlashCommandProvider()
    {
        var page = CreatePage();

        var strip = page.Input.SuggestionStrip;

        var match = strip.ComputeMatch("/mo", 3);
        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Items.Select(i => i.Replacement)).IsEquivalentTo(["/models"]);

        await Assert.That(strip.ComputeMatch("hello", 5)).IsNull();
    }
}
