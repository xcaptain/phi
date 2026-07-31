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
    public async Task FormatInvocation_ReadWithOffsetAndLimit_ShowsRange()
    {
        var args = new JsonObject
        {
            ["path"] = "a.cs",
            ["offset"] = 100,
            ["limit"] = 50,
        };
        await Assert.That(
            ToolCardRenderer.FormatInvocation(new ToolCall("id", "read") { Arguments = args }))
            .IsEqualTo("→ read a.cs [offset=100, limit=50]");
    }

    [Test]
    public async Task FormatInvocation_ReadWithOffsetOnly_UsesAllForLimit()
    {
        var args = new JsonObject { ["path"] = "a.cs", ["offset"] = 10 };
        await Assert.That(
            ToolCardRenderer.FormatInvocation(new ToolCall("id", "read") { Arguments = args }))
            .IsEqualTo("→ read a.cs [offset=10, limit=all]");
    }

    [Test]
    public async Task FormatInvocation_ReadWithoutRange_ShowsJustPath()
    {
        await Assert.That(ToolCardRenderer.FormatInvocation(Call("read", ("path", "a.cs"))))
            .IsEqualTo("→ read a.cs");
    }

    [Test]
    public async Task FormatSummary_Read_FullFile_ShowsTotalLines()
    {
        var result = new ToolResult(
            [new PhiAgent.TextBlock("...")],
            Details: ToolDetails.Node(
                new ReadDetails("a.cs", Offset: 1, Limit: 42, LineCount: 42, TotalLineCount: 42, ByteCount: 2048)));
        await Assert.That(ToolCardRenderer.FormatSummary("read", result))
            .IsEqualTo("read — 42 lines · 2.0KB");
    }

    [Test]
    public async Task FormatSummary_Read_Slice_ShowsRange()
    {
        var result = new ToolResult(
            [new PhiAgent.TextBlock("...")],
            Details: ToolDetails.Node(
                new ReadDetails("a.cs", Offset: 100, Limit: 50, LineCount: 50, TotalLineCount: 1234, ByteCount: 12_345)));
        await Assert.That(ToolCardRenderer.FormatSummary("read", result))
            .IsEqualTo("read — lines 100-149 of 1234 · 12.1KB");
    }

    [Test]
    public async Task FormatSummary_Read_SliceWithOffsetOneStillCountedAsSlice_WhenLimitLess()
    {
        // Even with Offset=1, a Limit smaller than the file shows the
        // "lines X-Y of T" form so the user knows more remains.
        var result = new ToolResult(
            [new PhiAgent.TextBlock("...")],
            Details: ToolDetails.Node(
                new ReadDetails("a.cs", Offset: 1, Limit: 10, LineCount: 10, TotalLineCount: 100, ByteCount: 800)));
        await Assert.That(ToolCardRenderer.FormatSummary("read", result))
            .IsEqualTo("read — lines 1-10 of 100 · 800B");
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
    public async Task FormatResultBody_Edit_RendersSideBySideDiff()
    {
        // EditDetails carries the old/new snippet; the renderer diffs them
        // side-by-side: red removed on the left, green added on the right.
        var result = new ToolResult(
            [new PhiAgent.TextBlock("ok")],
            Details: ToolDetails.Node(
                new EditDetails("b.txt", "line 1\nold line 2\nline 3", "line 1\nnew line 2\nline 3", "")));

        var body = ToolCardRenderer.FormatResultBody("edit", result);

        var grid = (XenoAtom.Terminal.UI.Controls.Grid)body;
        await Assert.That(grid.Cells.Count).IsEqualTo(2);
        var left = ((XenoAtom.Terminal.UI.Controls.Markup)grid.Cells[0].Content!).Text;
        var right = ((XenoAtom.Terminal.UI.Controls.Markup)grid.Cells[1].Content!).Text;
        await Assert.That(left).Contains("[red]- old line 2[/]");
        await Assert.That(right).Contains("[green]+ new line 2[/]");
        // Line numbers are present on both sides.
        await Assert.That(left).Contains("[dim]2 │ [/]");
    }

    private static string BodyText(string name, ToolResult result) =>
        ((XenoAtom.Terminal.UI.Controls.Markup)ToolCardRenderer.FormatResultBody(name, result)!).Text!;

    [Test]
    public async Task FormatResultBody_LongOutput_TruncatesWithHiddenNote()
    {
        var text = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var body = BodyText("bash", TextResult(text));

        await Assert.That(body).Contains("line 1");
        await Assert.That(body).DoesNotContain("line 20");
        await Assert.That(body).Contains("12 more lines");
    }

    [Test]
    public async Task FormatResultBody_Error_RendersRed()
    {
        var body = BodyText("bash", TextResult("boom", isError: true));
        await Assert.That(body).Contains("[red]boom[/]");
    }

    [Test]
    public async Task FormatResultBody_EscapesMarkupBrackets()
    {
        var body = BodyText("bash", TextResult("array[0] = [1]"));
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

    [Test]
    public async Task FormatResultBody_ReadSuccess_ReturnsEmptyMarkup()
    {
        // Boundary: the read tool's body must NOT pollute the transcript.
        var body = BodyText("read", TextResult("line 1\nline 2\nline 3"));
        await Assert.That(body).IsEqualTo("");
    }

    [Test]
    public async Task FormatResultBody_ReadError_StillReturnsErrorBody()
    {
        var body = BodyText(
            "read",
            TextResult("File not found: a.cs", isError: true));
        await Assert.That(body).Contains("[red]File not found: a.cs[/]");
    }
}
