using System.Text.Json.Nodes;
using DiffPlex.Renderer;
using PhiAgent;
using PhiCoding.Tools.Details;
using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class ToolCardRendererTests
{
    private static ToolCall Call(string name, params (string Key, string Value)[] args)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in args) obj[k] = v;
        return new ToolCall("id-1", name) { Arguments = obj };
    }

    private static ToolResult TextResult(string text, bool isError = false) =>
        new([new PhiAgent.TextBlock(text)], IsError: isError);

    [Test]
    public async Task FormatInvocation_Bash_ShowsDollarCommand()
    {
        var text = ToolCardRenderer.FormatInvocation(Call("bash", ("command", "ls -la")));
        await Assert.That(text).IsEqualTo("$ ls -la");
    }

    [Test]
    public async Task FormatInvocation_FileTools_ShowPath()
    {
        await Assert.That(ToolCardRenderer.FormatInvocation(Call("read", ("path", "a.cs"))))
            .IsEqualTo("→ read a.cs");
        await Assert.That(ToolCardRenderer.FormatInvocation(Call("write", ("path", "b.cs"))))
            .IsEqualTo("→ write b.cs");
        await Assert.That(ToolCardRenderer.FormatInvocation(Call("edit", ("path", "c.cs"))))
            .IsEqualTo("→ edit c.cs");
    }

    [Test]
    public async Task FormatInvocation_UnknownTool_FallsBackToName()
    {
        await Assert.That(ToolCardRenderer.FormatInvocation(Call("grep"))).IsEqualTo("→ grep");
    }

    [Test]
    public async Task FormatSummary_Read_ShowsLinesAndBytes()
    {
        var result = new ToolResult(
            [new PhiAgent.TextBlock("...")],
            Details: ToolDetails.Node(new ReadDetails("a.cs", 42, 2048)));
        await Assert.That(ToolCardRenderer.FormatSummary("read", result))
            .IsEqualTo("read — 42 lines · 2.0KB");
    }

    [Test]
    public async Task FormatSummary_Bash_ShowsExitCodeAndDuration()
    {
        var result = new ToolResult(
            [new PhiAgent.TextBlock("...")],
            Details: ToolDetails.Node(new BashDetails("ls", 1, 123, "", "")));
        await Assert.That(ToolCardRenderer.FormatSummary("bash", result))
            .IsEqualTo("bash — exit=1 in 123ms");
    }

    [Test]
    public async Task FormatSummary_WithoutDetails_FallsBackToName()
    {
        await Assert.That(ToolCardRenderer.FormatSummary("read", TextResult("x"))).IsEqualTo("read");
    }

    [Test]
    public async Task FormatResultBody_Edit_RendersColoredDiff()
    {
        var patch = UnidiffRenderer.GenerateUnidiff(
            "line 1\nold line 2\nline 3", "line 1\nnew line 2\nline 3", "a.txt", "b.txt");
        var result = new ToolResult(
            [new PhiAgent.TextBlock("ok")],
            Details: ToolDetails.Node(new EditDetails("b.txt", "old line 2", "new line 2", patch)));

        var body = ToolCardRenderer.FormatResultBody("edit", result);

        await Assert.That(body).Contains("[green]+new line 2[/]");
        await Assert.That(body).Contains("[red]-old line 2[/]");
        await Assert.That(body).Contains("[dim]@@");
    }

    [Test]
    public async Task FormatResultBody_LongOutput_TruncatesWithHiddenNote()
    {
        var text = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var body = ToolCardRenderer.FormatResultBody("bash", TextResult(text));

        await Assert.That(body).Contains("line 1");
        await Assert.That(body).DoesNotContain("line 20");
        await Assert.That(body).Contains("12 more lines");
    }

    [Test]
    public async Task FormatResultBody_Error_RendersRed()
    {
        var body = ToolCardRenderer.FormatResultBody("bash", TextResult("boom", isError: true));
        await Assert.That(body).Contains("[red]boom[/]");
    }

    [Test]
    public async Task FormatResultBody_EscapesMarkupBrackets()
    {
        var body = ToolCardRenderer.FormatResultBody("bash", TextResult("array[0] = [1]"));
        await Assert.That(body).Contains("array\\[0\\] = \\[1\\]");
    }

    [Test]
    public async Task TruncateLines_ShortText_KeepsAllLines()
    {
        var lines = ToolCardRenderer.TruncateLines("a\nb\nc", 8, 2000, out var hidden, out var charTruncated);
        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(hidden).IsEqualTo(0);
        await Assert.That(charTruncated).IsFalse();
    }
}
