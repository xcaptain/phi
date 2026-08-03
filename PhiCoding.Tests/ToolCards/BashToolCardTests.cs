using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding.Tools.Details;
using PhiCoding.Tui.ToolCards;
using XenoAtom.Terminal.UI.Rendering;

namespace PhiCoding.Tests.ToolCards;

[NotInParallel("tool-card-render-tests")]
public class BashToolCardTests
{
    private static ToolCall Call(string command)
        => new("id-1", "bash") { Arguments = new JsonObject { ["command"] = command } };

    private static ToolResult TextResult(string text, bool isError = false) =>
        new([new TextBlock(text)], IsError: isError);

    [Test]
    public async Task ShowPending_RendersDollarCommand()
    {
        var card = new BashToolCard();
        card.ShowPending(Call("ls -la"));

        await Assert.That(card.Title).IsEqualTo("[primary]$ ls -la[/]");
    }

    [Test]
    public async Task Complete_Success_AppendsExitAndDurationSummary()
    {
        var card = new BashToolCard();
        card.ShowPending(Call("ls"));
        card.Complete(new ToolResult(
            [new TextBlock("ok")],
            Details: ToolDetails.Node(new BashDetails("ls", ExitCode: 0, DurationMs: 42, Stdout: "", Stderr: ""))));

        await Assert.That(card.Title).Contains("[green]✓[/]");
        await Assert.That(card.Title).Contains("$ ls");
        await Assert.That(card.Title).Contains("bash — exit=0 in 42ms");
    }

    [Test]
    public async Task Complete_NonZeroExit_ShowsRedStatusGlyph()
    {
        var card = new BashToolCard();
        card.ShowPending(Call("false"));
        // BashTool maps non-zero ExitCode to IsError=true; mirror that here.
        card.Complete(new ToolResult(
            [new TextBlock("oops")],
            Details: ToolDetails.Node(new BashDetails("false", ExitCode: 1, DurationMs: 123, Stdout: "", Stderr: "")),
            IsError: true));

        await Assert.That(card.Title).Contains("[red]✗[/]");
        await Assert.That(card.Title).Contains("bash — exit=1 in 123ms");
    }

    [Test]
    public async Task Complete_Success_BodyIsDimTruncatedOutput()
    {
        var card = new BashToolCard();
        card.ShowPending(Call("ls"));
        card.Complete(TextResult("file1\nfile2"));

        var body = (XenoAtom.Terminal.UI.Controls.Markup)card.BodyState.Value;
        await Assert.That(body.Text).Contains("[dim]file1[/]");
        await Assert.That(body.Text).Contains("[dim]file2[/]");
    }

    [Test]
    public async Task Complete_Error_BodyIsRedTruncatedOutput()
    {
        var card = new BashToolCard();
        card.ShowPending(Call("false"));
        card.Complete(TextResult("boom", isError: true));

        var body = (XenoAtom.Terminal.UI.Controls.Markup)card.BodyState.Value;
        await Assert.That(body.Text).Contains("[red]boom[/]");
    }

    [Test]
    public async Task Complete_LongOutput_TruncatesWithHiddenNote()
    {
        var card = new BashToolCard();
        card.ShowPending(Call("ls"));
        var text = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        card.Complete(TextResult(text));

        var body = (XenoAtom.Terminal.UI.Controls.Markup)card.BodyState.Value;
        await Assert.That(body.Text).Contains("line 1");
        await Assert.That(body.Text).DoesNotContain("line 20");
        await Assert.That(body.Text).Contains("12 more lines");
    }

    [Test]
    public async Task Complete_EscapesBracketsInOutput()
    {
        var card = new BashToolCard();
        card.ShowPending(Call("ls"));
        card.Complete(TextResult("array[0] = [1]"));

        var body = (XenoAtom.Terminal.UI.Controls.Markup)card.BodyState.Value;
        await Assert.That(body.Text).Contains("array\\[0\\] = \\[1\\]");
    }

    [Test]
    public async Task Complete_NoDetails_FallsBackToBashName()
    {
        var card = new BashToolCard();
        card.ShowPending(Call("ls"));
        card.Complete(TextResult("interrupted"));

        await Assert.That(card.Title).Contains("· bash[/]");
    }

    private static string RenderCard(BashToolCard card, int width)
    {
        var buffer = VisualSnapshotRenderer.Render(card.Visual, width);
        return string.Join("\n", buffer.ToMarkupLines());
    }
}
