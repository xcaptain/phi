using PhiCoding.Tests.Helpers;

namespace PhiCoding.Tests;

[NotInParallel("phi-tui-suggestion-tests")]
public class PhiTuiAppSuggestionTests
{
    [Test]
    public async Task BuildRoot_WiresSuggestionStrip_WithSlashProvider()
    {
        var app = new PhiCoding.Tui.PhiTuiApp(new MockSession());
        app.BuildRoot();

        await Assert.That(app.SuggestionStrip).IsNotNull();
    }

    [Test]
    public async Task WiredStrip_UsesSlashCommandProvider()
    {
        var app = new PhiCoding.Tui.PhiTuiApp(new MockSession());
        app.BuildRoot();

        var strip = app.SuggestionStrip!;

        var match = strip.ComputeMatch("/mo", 3);
        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Items.Select(i => i.Replacement)).IsEquivalentTo(["/models"]);

        await Assert.That(strip.ComputeMatch("hello", 5)).IsNull();
    }
}
