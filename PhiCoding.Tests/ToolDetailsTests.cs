using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding.Tools;
using PhiCoding.Tools.Details;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class ReadToolDetailsTests
{
    [Test]
    public async Task ExecuteAsync_ExistingFile_ReturnsReadDetails()
    {
        using var file = new TempFile("line 1\nline 2\nline 3");
        var tool = new ReadTool();
        var args = JsonNode.Parse($$"""{"path":"{{file.Path}}"}""")!;

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
            var args = JsonNode.Parse($$"""{"path":"{{path}}","content":"hello"}""")!;
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
        var args = JsonNode.Parse($$"""{"path":"{{file.Path}}","content":"new"}""")!;

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
        var args = JsonNode.Parse($$"""
            {"path":"{{file.Path}}","oldString":"bar","newString":"BAR"}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        var details = ToolDetails.Read<EditDetails>(result.Details);
        await Assert.That(details).IsNotNull();
        await Assert.That(details!.Path).IsEqualTo(file.Path);
        await Assert.That(details.OldString).IsEqualTo("bar");
        await Assert.That(details.NewString).IsEqualTo("BAR");
        await Assert.That(details.Patch).IsNotNull();
        await Assert.That(details.Patch).Contains("-bar");
        await Assert.That(details.Patch).Contains("+BAR");
    }
}

public class BashToolDetailsTests
{
    [Test]
    public async Task ExecuteAsync_Success_CapturesExitCodeAndDuration()
    {
        var tool = new BashTool();
        var args = JsonNode.Parse("""{"command":"echo hello"}""")!;

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
        var args = JsonNode.Parse("""{"command":"exit 7"}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        var details = ToolDetails.Read<BashDetails>(result.Details);
        await Assert.That(details!.ExitCode).IsEqualTo(7);
    }
}