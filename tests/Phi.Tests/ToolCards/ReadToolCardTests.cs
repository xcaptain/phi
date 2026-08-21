using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Tui.Components.ToolCards;
using XenoAtom.Terminal.UI.Rendering;

namespace Phi.Tests.ToolCards;

[NotInParallel(TuiTestGroups.BindingManager)]
public class ReadToolCardTests
{
    private static ToolCall Call(string path, int? offset = null, int? limit = null)
    {
        var args = new JsonObject { ["path"] = path };
        if (offset is not null) args["offset"] = offset.Value;
        if (limit is not null) args["limit"] = limit.Value;
        return new ToolCall("id-1", "read") { Arguments = args };
    }

    private static ToolResult TextResult(string text, bool isError = false) =>
        new([new TextBlock(text)], IsError: isError);

    [Test]
    public async Task ShowPending_RendersDimInvocation_WithoutEscapingRangeHint()
    {
        var card = new ReadToolCard();
        card.ShowPending(Call("a.cs", offset: 100, limit: 50));

        // The literal range hint must pass through unescaped so XenoAtom
        // renders the unknown tag literally instead of stripping it.
        await Assert.That(card.Title).IsEqualTo("[dim]→ read a.cs [offset=100, limit=50][/]");
    }

    [Test]
    public async Task ShowPending_OffsetOnly_UsesAllForLimit()
    {
        var card = new ReadToolCard();
        card.ShowPending(Call("a.cs", offset: 10));

        await Assert.That(card.Title).IsEqualTo("[dim]→ read a.cs [offset=10, limit=all][/]");
    }

    [Test]
    public async Task ShowPending_NoRange_ShowsJustPath()
    {
        var card = new ReadToolCard();
        card.ShowPending(Call("a.cs"));

        await Assert.That(card.Title).IsEqualTo("[dim]→ read a.cs[/]");
    }

    [Test]
    public async Task Complete_FullFile_AppendsLinesAndBytesSummary()
    {
        var card = new ReadToolCard();
        card.ShowPending(Call("a.cs"));
        card.Complete(new ToolResult(
            [new TextBlock("...")],
            Details: Phi.Tools.Details.ToolDetails.Node(
                new Phi.Tools.Details.ReadDetails(
                    "a.cs", Offset: 1, Limit: 42, LineCount: 42, TotalLineCount: 42, ByteCount: 2048))));

        await Assert.That(card.Title).Contains("[green]✓[/]");
        await Assert.That(card.Title).Contains("→ read a.cs");
        await Assert.That(card.Title).Contains("read — 42 lines · 2.0KB");
    }

    [Test]
    public async Task Complete_Slice_ShowsReturnedRange()
    {
        var card = new ReadToolCard();
        card.ShowPending(Call("a.cs"));
        card.Complete(new ToolResult(
            [new TextBlock("...")],
            Details: Phi.Tools.Details.ToolDetails.Node(
                new Phi.Tools.Details.ReadDetails(
                    "a.cs", Offset: 100, Limit: 50, LineCount: 50, TotalLineCount: 1234, ByteCount: 12_345))));

        await Assert.That(card.Title).Contains("read — lines 100-149 of 1234 · 12.1KB");
    }

    [Test]
    public async Task Complete_OffsetOneWithLimit_StillShowsSlice()
    {
        var card = new ReadToolCard();
        card.ShowPending(Call("a.cs"));
        card.Complete(new ToolResult(
            [new TextBlock("...")],
            Details: Phi.Tools.Details.ToolDetails.Node(
                new Phi.Tools.Details.ReadDetails(
                    "a.cs", Offset: 1, Limit: 10, LineCount: 10, TotalLineCount: 100, ByteCount: 800))));

        await Assert.That(card.Title).Contains("read — lines 1-10 of 100 · 800B");
    }

    [Test]
    public async Task Complete_NoDetails_FallsBackToName()
    {
        var card = new ReadToolCard();
        card.ShowPending(Call("a.cs"));
        card.Complete(TextResult("x"));

        await Assert.That(card.Title).Contains("· read[/]");
    }

    [Test]
    public async Task Complete_Error_ShowsRedStatusGlyph()
    {
        var card = new ReadToolCard();
        card.ShowPending(Call("a.cs"));
        card.Complete(TextResult("File not found", isError: true));

        await Assert.That(card.Title).Contains("[red]✗[/]");
    }

    [Test]
    public async Task Rendered_AsSingleLineMarkup_NoGroupWrapper()
    {
        // Regression: read tool must render as ONE Markup line, not a
        // Group(title, body) card.
        var card = new ReadToolCard();
        card.ShowPending(Call("a.cs", offset: 30, limit: 18));
        card.Complete(new ToolResult(
            [new TextBlock("file body")],
            Details: Phi.Tools.Details.ToolDetails.Node(
                new Phi.Tools.Details.ReadDetails(
                    "a.cs", Offset: 30, Limit: 18, LineCount: 18, TotalLineCount: 82, ByteCount: 2048))));

        await Assert.That(card.Visual).IsTypeOf<XenoAtom.Terminal.UI.Controls.Markup>();

        var rendered = RenderCard(card, width: 100);
        await Assert.That(rendered).Contains("[offset=30, limit=18]");
        await Assert.That(rendered).Contains("read — lines 30-47 of 82");
        await Assert.That(rendered).DoesNotContain("\\[");  // brackets unescaped
    }

    private static string RenderCard(ReadToolCard card, int width)
    {
        var buffer = VisualSnapshotRenderer.Render(card.Visual, width);
        return string.Join("\n", buffer.ToMarkupLines());
    }
}
