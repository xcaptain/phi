using System.Text.Json.Nodes;
using Phi.Extensions.CodingPack.Tools;
using Phi.Extensions.CodingPack.Tools.Details;

namespace Phi.Tests;

public class ReadToolDetailsTests
{
    [Test]
    public async Task ExecuteAsync_ExistingFile_ReturnsReadDetails()
    {
        using var file = new TempFile("line 1\nline 2\nline 3");
        var tool = new ReadTool();
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{file.Path}}"}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        var details = ToolDetails.Read<ReadDetails>(result.Details);
        await Assert.That(details).IsNotNull();
        await Assert.That(details!.Path).IsEqualTo(file.Path);
        await Assert.That(details.LineCount).IsEqualTo(3);
        await Assert.That(details.ByteCount).IsGreaterThan(0);
    }
}

public class WriteToolDetailsTests
{
    [Test]
    public async Task ExecuteAsync_NewFile_ModeIsCreated()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"phi-new-{Guid.NewGuid():N}.txt");
        try
        {
            var tool = new WriteTool();
            var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{path}}","content":"hello"}""")!;
            var result = await tool.ExecuteAsync("c1", args, default);

            await Assert.That(result.IsError).IsFalse();
            var details = ToolDetails.Read<WriteDetails>(result.Details);
            await Assert.That(details).IsNotNull();
            await Assert.That(details!.Mode).IsEqualTo("created");
            await Assert.That(details.BytesWritten).IsEqualTo(5);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public async Task ExecuteAsync_ExistingFile_ModeIsOverwrote()
    {
        using var file = new TempFile("old");
        var tool = new WriteTool();
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{file.Path}}","content":"new"}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        var details = ToolDetails.Read<WriteDetails>(result.Details);
        await Assert.That(details!.Mode).IsEqualTo("overwrote");
    }
}

public class EditToolDetailsTests
{
    [Test]
    public async Task ExecuteAsync_UniqueMatch_EmitsEditDetailsWithPatch()
    {
        using var file = new TempFile("foo\nbar\nbaz");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{file.Path}}","edits":[{"oldText":"bar","newText":"BAR"}]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        var details = ToolDetails.Read<EditDetails>(result.Details);
        await Assert.That(details).IsNotNull();
        await Assert.That(details!.Path).IsEqualTo(file.Path);
        await Assert.That(details.Edits.Count).IsEqualTo(1);
        await Assert.That(details.Edits[0].OldText).IsEqualTo("bar");
        await Assert.That(details.Edits[0].NewText).IsEqualTo("BAR");
        await Assert.That(details.Patch).IsNotNull();
        await Assert.That(details.Patch).Contains("-bar");
        await Assert.That(details.Patch).Contains("+BAR");
    }

    [Test]
    public async Task ExecuteAsync_MultipleEdits_AppliesAllAndReportsCount()
    {
        using var file = new TempFile("one\ntwo\nthree\nfour");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{file.Path}}","edits":[
                {"oldText":"one","newText":"ONE"},
                {"oldText":"three","newText":"THREE"}
            ]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("ONE\ntwo\nTHREE\nfour");
        var details = ToolDetails.Read<EditDetails>(result.Details);
        await Assert.That(details!.Edits.Count).IsEqualTo(2);
        await Assert.That(details.Edits.All(e => e.FirstLine >= 1)).IsTrue();
        await Assert.That(details.Edits[0].FirstLine).IsEqualTo(1);  // "one" is on line 1
        await Assert.That(details.Edits[1].FirstLine).IsEqualTo(3);  // "three" is on line 3
    }

    [Test]
    public async Task ExecuteAsync_OverlappingEdits_Rejected()
    {
        using var file = new TempFile("hello world");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{file.Path}}","edits":[
                {"oldText":"hello","newText":"H"},
                {"oldText":"lo wo","newText":"X"}
            ]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("overlap");
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("hello world");
    }

    [Test]
    public async Task ExecuteAsync_CrlfFile_PreservesLineEndings()
    {
        using var file = new TempFile("one\r\ntwo\r\nthree");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{file.Path}}","edits":[{"oldText":"two","newText":"TWO"}]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("one\r\nTWO\r\nthree");
    }

    [Test]
    public async Task ExecuteAsync_BomFile_PreservesBom()
    {
        using var file = new TempFile("\uFEFFline one\nline two");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{file.Path}}","edits":[{"oldText":"line one","newText":"LINE ONE"}]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        // Read raw bytes so the UTF-8 BOM is not stripped by ReadAllText.
        var bytes = File.ReadAllBytes(file.Path);
        await Assert.That(bytes.Length).IsGreaterThan(3);
        await Assert.That(bytes[0]).IsEqualTo((byte)0xEF);
        await Assert.That(bytes[1]).IsEqualTo((byte)0xBB);
        await Assert.That(bytes[2]).IsEqualTo((byte)0xBF);
        await Assert.That(System.Text.Encoding.UTF8.GetString(bytes)).Contains("LINE ONE");
    }
}

public class BashToolDetailsTests
{
    [Test]
    public async Task ExecuteAsync_Success_CapturesExitCodeAndDuration()
    {
        var tool = new BashTool();
        var args = (JsonObject)JsonNode.Parse("""{"command":"echo hello"}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        var details = ToolDetails.Read<BashDetails>(result.Details);
        await Assert.That(details).IsNotNull();
        await Assert.That(details!.Command).IsEqualTo("echo hello");
        await Assert.That(details.ExitCode).IsEqualTo(0);
        await Assert.That(details.Stdout).Contains("hello");
        await Assert.That(details.Stderr).IsEqualTo("");
        await Assert.That(details.DurationMs).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_NonZeroExit_MarksErrorAndCapturesExitCode()
    {
        var tool = new BashTool();
        var args = (JsonObject)JsonNode.Parse("""{"command":"exit 7"}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        var details = ToolDetails.Read<BashDetails>(result.Details);
        await Assert.That(details!.ExitCode).IsEqualTo(7);
    }
}
