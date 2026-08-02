using PhiAgent;
using PhiCoding.Tui;
using XenoAtom.Terminal.UI.Rendering;

namespace PhiCoding.Tests;

[NotInParallel("phi-status-bar-render-tests")]
public class PhiStatusBarTests
{
    [Test]
    public async Task FormatCount_SmallNumbers_Raw()
    {
        await Assert.That(PhiStatusBar.FormatCount(0)).IsEqualTo("0");
        await Assert.That(PhiStatusBar.FormatCount(999)).IsEqualTo("999");
    }

    [Test]
    public async Task FormatCount_Thousands_KSuffix()
    {
        await Assert.That(PhiStatusBar.FormatCount(1500)).IsEqualTo("1.5k");
        await Assert.That(PhiStatusBar.FormatCount(999_999)).IsEqualTo("1000.0k");
    }

    [Test]
    public async Task FormatCount_Millions_MSuffix()
    {
        await Assert.That(PhiStatusBar.FormatCount(2_500_000)).IsEqualTo("2.5M");
    }

    [Test]
    public async Task ShortenPath_UnderHome_UsesTilde()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await Assert.That(PhiStatusBar.ShortenPath(home + "/github/phi")).IsEqualTo("~/github/phi");
    }

    [Test]
    public async Task ShortenPath_OutsideHome_Unchanged()
    {
        await Assert.That(PhiStatusBar.ShortenPath("/var/tmp/x")).IsEqualTo("/var/tmp/x");
    }

    [Test]
    public async Task Renders_Ready_And_Model_On_Init()
    {
        var bar = new PhiStatusBar("phi-3");
        var line = RenderBar(bar, width: 120);

        await Assert.That(line).Contains("ready");
        await Assert.That(line).Contains("phi-3");
    }

    [Test]
    public async Task TurnStartEvent_Shows_Running_Turn()
    {
        var bar = new PhiStatusBar("phi-3");
        bar.Apply(new TurnStartEvent(2));

        var line = RenderBar(bar, width: 120);

        await Assert.That(line).Contains("running");
        await Assert.That(line).Contains("turn 2");
        await Assert.That(line).DoesNotContain("ready");
    }

    [Test]
    public async Task TurnEndEvent_Reverts_To_Ready()
    {
        var bar = new PhiStatusBar("phi-3");
        bar.Apply(new TurnStartEvent(1));
        bar.Apply(new TurnEndEvent(new AssistantMessage()));

        var line = RenderBar(bar, width: 120);

        await Assert.That(line).Contains("ready");
        await Assert.That(line).DoesNotContain("running");
    }

    [Test]
    public async Task QueuedCount_While_Idle_Shows_QueuedBadge()
    {
        var bar = new PhiStatusBar("phi-3");
        bar.QueuedCount.Value = 3;

        var line = RenderBar(bar, width: 120);

        await Assert.That(line).Contains("ready");
        await Assert.That(line).Contains("+3 queued");
    }

    [Test]
    public async Task Running_And_Queued_Shows_Both()
    {
        var bar = new PhiStatusBar("phi-3");
        bar.Apply(new TurnStartEvent(1));
        bar.QueuedCount.Value = 2;

        var line = RenderBar(bar, width: 120);

        await Assert.That(line).Contains("running");
        await Assert.That(line).Contains("turn 1");
        await Assert.That(line).Contains("+2 queued");
    }

    [Test]
    public async Task UpdateStats_Appends_TokenCounts()
    {
        var bar = new PhiStatusBar("phi-3");
        bar.UpdateStats(new SessionStats(0, 0, 1500, 800, 2300, null));

        var line = RenderBar(bar, width: 120);

        await Assert.That(line).Contains("↑1.5k");
        await Assert.That(line).Contains("↓800");
    }

    [Test]
    public async Task UpdateContext_With_Threshold_Shows_Fraction()
    {
        var bar = new PhiStatusBar("phi-3");
        bar.UpdateContext(2000, 8000);

        var line = RenderBar(bar, width: 120);

        var expectedDenominator = PhiStatusBar.FormatCount(8000 + ContextWindow.DefaultCompactionReserveTokens);
        await Assert.That(line).Contains($"2.0k/{expectedDenominator}");
    }

    [Test]
    public async Task UpdateContext_Without_Threshold_Shows_Raw()
    {
        var bar = new PhiStatusBar("phi-3");
        bar.UpdateContext(2000, null);

        var line = RenderBar(bar, width: 120);

        await Assert.That(line).Contains("2.0k");
        await Assert.That(line).DoesNotContain("2.0k/");
    }

    private static string RenderBar(PhiStatusBar bar, int width)
    {
        var buffer = VisualSnapshotRenderer.Render(bar.Visual, width);
        return string.Join("\n", buffer.ToMarkupLines());
    }
}
