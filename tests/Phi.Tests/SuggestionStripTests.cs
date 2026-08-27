using Phi.Prompt;
using Phi.Tui.Components;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

namespace Phi.Tests;

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

    [Test]
    public async Task IsActiveSlashOnFirstLine_RecognizesBufferStartTrigger()
    {
        // Fast path mirrors the built-in provider trigger: the buffer's
        // very first character must be '/' AND the caret must still sit
        // on the first line. Mid-sentence slashes, indented lines, and
        // slashes on continuation lines all fail to qualify.
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("   ")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("hello")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("hello world")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("hello /world")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("  /exit")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("hello\n/exit")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("hello\n/")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("/exit\nfoo")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("/exit\n")).IsFalse();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("/")).IsTrue();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("/co")).IsTrue();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("/exit")).IsTrue();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("/skill:foo")).IsTrue();
        await Assert.That(SuggestionStrip.IsActiveSlashOnFirstLine("/connect openai")).IsTrue();
    }

    [Test]
    public async Task Build_NotFirstLineSlash_SkipsProviderPipeline()
    {
        // The fast path must short-circuit before any provider sees the
        // input; otherwise we'd pay the provider foreach + candidate list
        // allocation on every keystroke while the user types a normal
        // prompt — and, after the strict first-line rule, even while the
        // user types a mid-sentence slash or hits Cmd+Enter to break a
        // line. VisualSnapshotRenderer.Render doesn't drive PrepareChildren
        // (it goes straight to Measure/Arrange), so we invoke the builder
        // directly through reflection — that's the actual code path the
        // dependency tracker runs on every keystroke.
        var provider = new CountingProvider();
        var strip = new SuggestionStrip(new State<string?>(null), [provider]);
        var build = typeof(SuggestionStrip).GetMethod(
            "Build",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        // Plain prose, mid-sentence slashes, indented lines, and
        // continuation-line slashes must all skip the provider pipeline.
        foreach (var text in new[]
                 {
                     "h", "he", "hel", "hello",
                     "hello /exit", "  /exit",
                     "hello\n/exit", "hello\n/",
                     "/exit\nfoo", "/exit\n",
                 })
        {
            strip.Text.Value = text;
            build.Invoke(strip, null);
        }
        await Assert.That(provider.CallCount).IsEqualTo(0);

        // First-line slash must engage the provider pipeline.
        foreach (var text in new[] { "/", "/m" })
        {
            strip.Text.Value = text;
            build.Invoke(strip, null);
        }
        await Assert.That(provider.CallCount).IsEqualTo(2);
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

    private sealed class CountingProvider : ISuggestionProvider
    {
        public int CallCount { get; private set; }

        public SuggestionMatch? GetSuggestion(ReadOnlySpan<char> text, int caret)
        {
            CallCount++;
            // Mirror the production trigger: buffer starts with '/' AND
            // the caret sits on the first line (no '\n' before it). The
            // strip's fast path only forwards inputs that satisfy this
            // contract, so reaching this body implies the gate passed.
            if (text.Length == 0 || caret <= 0 || text[0] != '/')
                return null;
            for (var i = 0; i < caret; i++)
            {
                if (text[i] == '\n') return null;
            }
            return new SuggestionMatch(0, caret, [new SuggestionItem("x", "y", "x")]);
        }
    }
}
