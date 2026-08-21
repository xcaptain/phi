using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Tui.Components.ToolCards;
using XenoAtom.Terminal.UI.Controls;

namespace Phi.Tests.ToolCards;

[NotInParallel(TuiTestGroups.BindingManager)]
public class GenericToolCardTests
{
    private static ToolCall Call(string name, params (string Key, string Value)[] args)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in args) obj[k] = v;
        return new ToolCall("id-1", name) { Arguments = obj };
    }

    private static ToolResult TextResult(string text, bool isError = false) =>
        new([new Phi.Agent.TextBlock(text)], IsError: isError);

    [Test]
    public async Task ShowPending_RendersArrowAndName()
    {
        var card = new GenericToolCard();
        card.ShowPending(Call("grep"));

        await Assert.That(card.Title).IsEqualTo("[primary]→ grep[/]");
    }

    [Test]
    public async Task Complete_Success_ShowsGreenCheckAndTruncatedBody()
    {
        var card = new GenericToolCard();
        card.ShowPending(Call("grep"));
        card.Complete(TextResult("matched line"));

        await Assert.That(card.Title).Contains("[green]✓[/]");
        await Assert.That(card.Title).Contains("→ grep");
        var body = (Markup)card.BodyState.Value;
        await Assert.That(body.Text).Contains("[dim]matched line[/]");
    }

    [Test]
    public async Task Complete_Error_BodyIsRedTruncated()
    {
        var card = new GenericToolCard();
        card.ShowPending(Call("grep"));
        card.Complete(TextResult("not found", isError: true));

        await Assert.That(card.Title).Contains("[red]✗[/]");
        var body = (Markup)card.BodyState.Value;
        await Assert.That(body.Text).Contains("[red]not found[/]");
    }
}
