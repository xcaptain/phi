using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding.Tools.Details;
using PhiCoding.Tui.Components.ToolCards;
using XenoAtom.Terminal.UI.Rendering;

namespace PhiCoding.Tests.ToolCards;

[NotInParallel(TuiTestGroups.BindingManager)]
public class EditToolCardTests
{
    private static ToolCall Call(string path)
        => new("id-1", "edit") { Arguments = new JsonObject { ["path"] = path } };

    private static ToolResult TextResult(string text, bool isError = false) =>
        new([new TextBlock(text)], IsError: isError);

    [Test]
    public async Task ShowPending_RendersArrowAndPath()
    {
        var card = new EditToolCard();
        card.ShowPending(Call("c.cs"));

        await Assert.That(card.Title).IsEqualTo("[primary]→ edit c.cs[/]");
    }

    [Test]
    public async Task Complete_Success_AppendsBlockCountSummary()
    {
        var card = new EditToolCard();
        card.ShowPending(Call("c.cs"));
        card.Complete(new ToolResult(
            [new TextBlock("ok")],
            Details: ToolDetails.Node(new EditDetails(
                "c.cs",
                [new EditOpDetails("a", "b"), new EditOpDetails("c", "d")],
                Diff: "",
                Patch: "",
                FirstChangedLine: null))));

        await Assert.That(card.Title).Contains("[green]✓[/]");
        await Assert.That(card.Title).Contains("→ edit c.cs");
        await Assert.That(card.Title).Contains("edit c.cs · 2 block(s)");
    }

    [Test]
    public async Task Complete_Success_BodyIsSideBySideDiffGrid()
    {
        var card = new EditToolCard();
        card.ShowPending(Call("c.cs"));
        card.Complete(new ToolResult(
            [new TextBlock("ok")],
            Details: ToolDetails.Node(new EditDetails(
                "c.cs",
                [new EditOpDetails("line 1\nold line 2\nline 3", "line 1\nnew line 2\nline 3")],
                Diff: "",
                Patch: "",
                FirstChangedLine: null))));

        await Assert.That(card.BodyState.Value).IsTypeOf<XenoAtom.Terminal.UI.Controls.Grid>();
        var grid = (XenoAtom.Terminal.UI.Controls.Grid)card.BodyState.Value;
        await Assert.That(grid.Cells.Count).IsEqualTo(2);

        var left = ((XenoAtom.Terminal.UI.Controls.Markup)grid.Cells[0].Content!).Text;
        var right = ((XenoAtom.Terminal.UI.Controls.Markup)grid.Cells[1].Content!).Text;
        await Assert.That(left).Contains("[red]- old line 2[/]");
        await Assert.That(right).Contains("[green]+ new line 2[/]");
        await Assert.That(left).Contains("[dim]2 │ [/]");
    }

    [Test]
    public async Task Complete_Error_BodyIsRedTruncatedMarkup()
    {
        var card = new EditToolCard();
        card.ShowPending(Call("c.cs"));
        card.Complete(TextResult("oldText not found", isError: true));

        await Assert.That(card.Title).Contains("[red]✗[/]");
        var body = (XenoAtom.Terminal.UI.Controls.Markup)card.BodyState.Value;
        await Assert.That(body.Text).Contains("[red]oldText not found[/]");
    }

    [Test]
    public async Task Complete_NoDetails_FallsBackToEditName()
    {
        var card = new EditToolCard();
        card.ShowPending(Call("c.cs"));
        card.Complete(TextResult("validation failed", isError: true));

        await Assert.That(card.Title).Contains("· edit[/]");
    }

    [Test]
    public async Task Rendered_VisualIsGroup_TitleAndGrid()
    {
        var card = new EditToolCard();
        card.ShowPending(Call("c.cs"));
        card.Complete(new ToolResult(
            [new TextBlock("ok")],
            Details: ToolDetails.Node(new EditDetails(
                "c.cs",
                [new EditOpDetails("line 1\nold line 2\nline 3", "line 1\nnew line 2\nline 3")],
                Diff: "",
                Patch: "",
                FirstChangedLine: null))));

        // Render the full card Visual; the title shows up as markup text
        // and the body Grid (carrying the diff Markup pair) sits below.
        // Detailed diff color assertions live in Complete_Success_BodyIsSideBySideDiffGrid.
        var rendered = RenderCard(card, width: 100);
        await Assert.That(rendered).Contains("→ edit c.cs");
        await Assert.That(rendered).Contains("1 block(s)");
    }

    private static string RenderCard(EditToolCard card, int width)
    {
        var buffer = VisualSnapshotRenderer.Render(card.Visual, width);
        return string.Join("\n", buffer.ToMarkupLines());
    }
}
