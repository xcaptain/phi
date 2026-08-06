using PhiCoding.Tui.Components;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace PhiCoding.Tests;

[NotInParallel(TuiTestGroups.BindingManager)]
public class SuggestionStripTests
{
    private static string Render(SuggestionStrip strip, int width = 100)
    {
        var buffer = VisualSnapshotRenderer.Render(strip.Visual, width);
        return string.Join("\n", buffer.ToMarkupLines());
    }

    [Test]
    public async Task ComputeMatch_NoSlash_ReturnsNull()
    {
        var strip = new SuggestionStrip(new State<string?>(null), [new SlashCommandProvider()]);

        await Assert.That(strip.ComputeMatch("hello", 5)).IsNull();
        await Assert.That(strip.ComputeMatch("", 0)).IsNull();
        await Assert.That(strip.ComputeMatch(null, 0)).IsNull();
    }

    [Test]
    public async Task ComputeMatch_SlashToken_ReturnsFilteredItems()
    {
        var strip = new SuggestionStrip(new State<string?>(null), [new SlashCommandProvider()]);

        var match = strip.ComputeMatch("/mo", 3);

        await Assert.That(match!.Items.Select(i => i.Replacement)).IsEquivalentTo(["/models"]);
    }

    [Test]
    public async Task ComputeMatch_FirstProviderWins()
    {
        var custom = new SuggestionItem("custom", "a custom suggestion", "custom");
        var provider = new StaticProvider(custom);
        var strip = new SuggestionStrip(new State<string?>(null), [provider, new SlashCommandProvider()]);

        var match = strip.ComputeMatch("anything", 7);

        await Assert.That(match!.Items.Single().Replacement).IsEqualTo("custom");
    }

    [Test]
    public async Task Render_EmptyInput_CollapsesToNothing()
    {
        var strip = new SuggestionStrip(new State<string?>(null), [new SlashCommandProvider()]);

        var line = Render(strip);

        await Assert.That(line).DoesNotContain("/connect");
        await Assert.That(line).DoesNotContain("Connect");
    }

    [Test]
    public async Task Render_Prefix_ShowsOnlyMatchingCommandsWithDescriptions()
    {
        var strip = new SuggestionStrip(new State<string?>(null), [new SlashCommandProvider()]);
        strip.Text.Value = "/co";

        var line = Render(strip);

        await Assert.That(line).Contains("/connect");
        await Assert.That(line).Contains("Connect an LLM provider");
        await Assert.That(line).DoesNotContain("/models");
    }

    [Test]
    public async Task Render_AllCommands_TwoRowCompactLayout()
    {
        var strip = new SuggestionStrip(new State<string?>(null), [new SlashCommandProvider()]);
        strip.Text.Value = "/";

        var lines = RenderLines(strip, width: 200);

        // Row 1: command chips. Row 2: best-match description. No tall list.
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0]).Contains("/new");
        await Assert.That(lines[0]).Contains("/connect");
        await Assert.That(lines[0]).Contains("/models");
        await Assert.That(lines[0]).Contains("/sessions");
        await Assert.That(lines[0]).Contains("/exit");
        await Assert.That(lines[1]).Contains("Start a new, empty session");
    }

    [Test]
    public async Task Render_NonSlashText_Collapses()
    {
        var strip = new SuggestionStrip(new State<string?>(null), [new SlashCommandProvider()]);
        strip.Text.Value = "hello world";

        var line = Render(strip);

        await Assert.That(line).DoesNotContain("/connect");
        await Assert.That(line).DoesNotContain("Connect an LLM provider");
    }

    private static string[] RenderLines(SuggestionStrip strip, int width)
    {
        var buffer = VisualSnapshotRenderer.Render(strip.Visual, width);
        return [.. buffer.ToMarkupLines()];
    }

    private sealed class StaticProvider(params SuggestionItem[] items) : ISuggestionProvider
    {
        public SuggestionMatch? GetSuggestion(ReadOnlySpan<char> text, int caret) =>
            new(0, caret, items);
    }
}
