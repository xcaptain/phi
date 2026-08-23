using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Extensions.CodingPack.Tools.Details;
using Phi.Tui.Components.ToolCards;
using XenoAtom.Terminal.UI.Rendering;

namespace Phi.Tests.ToolCards;

[NotInParallel(TuiTestGroups.BindingManager)]
public class WriteToolCardTests
{
    private static ToolCall Call(string path)
        => new("id-1", "write") { Arguments = new JsonObject { ["path"] = path } };

    private static ToolResult TextResult(string text, bool isError = false) =>
        new([new TextBlock(text)], IsError: isError);

    [Test]
    public async Task ShowPending_RendersArrowAndPath()
    {
        var card = new WriteToolCard();
        card.ShowPending(Call("b.cs"));

        await Assert.That(card.Title).IsEqualTo("[primary]→ write b.cs[/]");
    }

    [Test]
    public async Task Complete_Success_AppendsBytesAndModeSummary()
    {
        var card = new WriteToolCard();
        card.ShowPending(Call("b.cs"));
        card.Complete(new ToolResult(
            [new TextBlock("ok")],
            Details: ToolDetails.Node(new WriteDetails("b.cs", BytesWritten: 1024, Mode: "created"))));

        await Assert.That(card.Title).Contains("[green]✓[/]");
        await Assert.That(card.Title).Contains("→ write b.cs");
        await Assert.That(card.Title).Contains("write — 1024 bytes (created)");
        // Body is empty markup on success — no leaked output text.
        var body = (XenoAtom.Terminal.UI.Controls.Markup)card.BodyState.Value;
        await Assert.That(body.Text).IsEqualTo("");
    }

    [Test]
    public async Task Complete_Error_BodyUsesRedMarkup()
    {
        var card = new WriteToolCard();
        card.ShowPending(Call("b.cs"));
        card.Complete(TextResult("permission denied", isError: true));

        await Assert.That(card.Title).Contains("[red]✗[/]");
        var body = (XenoAtom.Terminal.UI.Controls.Markup)card.BodyState.Value;
        await Assert.That(body.Text).Contains("[red]permission denied[/]");
    }

    [Test]
    public async Task Complete_EscapesBracketsInErrorBody()
    {
        var card = new WriteToolCard();
        card.ShowPending(Call("b.cs"));
        card.Complete(TextResult("array[0] = [1]", isError: true));

        var body = (XenoAtom.Terminal.UI.Controls.Markup)card.BodyState.Value;
        await Assert.That(body.Text).Contains("array\\[0\\] = \\[1\\]");
    }

    [Test]
    public async Task Rendered_ShowsCompletedTitle()
    {
        var card = new WriteToolCard();
        card.ShowPending(Call("b.cs"));
        card.Complete(new ToolResult(
            [new TextBlock("ok")],
            Details: ToolDetails.Node(new WriteDetails("b.cs", 42, "overwrote"))));

        var rendered = RenderCard(card, width: 100);
        await Assert.That(rendered).Contains("→ write b.cs");
        await Assert.That(rendered).Contains("write — 42 bytes (overwrote)");
    }

    private static string RenderCard(WriteToolCard card, int width)
    {
        var buffer = VisualSnapshotRenderer.Render(card.Visual, width);
        return string.Join("\n", buffer.ToMarkupLines());
    }
}
