using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Tests.Helpers;

namespace PhiCoding.Tests;

[NotInParallel(TuiTestGroups.BindingManager)]
public class PhiTuiAppSuggestionTests
{
    [Test]
    public async Task BuildRoot_WiresSuggestionStrip_WithSlashProvider()
    {
        var app = new PhiCoding.Tui.PhiTuiApp(
            new FakeSessionNavigator(new MockSession(), new ChatRoute(new ExistingSessionRequest("x"))),
            new ProviderManager());
        app.BuildRoot();

        await Assert.That(app.SuggestionStrip).IsNotNull();
    }

    [Test]
    public async Task WiredStrip_UsesSlashCommandProvider()
    {
        var app = new PhiCoding.Tui.PhiTuiApp(
            new FakeSessionNavigator(new MockSession(), new ChatRoute(new ExistingSessionRequest("x"))),
            new ProviderManager());
        app.BuildRoot();

        var strip = app.SuggestionStrip!;

        var match = strip.ComputeMatch("/mo", 3);
        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Items.Select(i => i.Replacement)).IsEquivalentTo(["/models"]);

        await Assert.That(strip.ComputeMatch("hello", 5)).IsNull();
    }
}
