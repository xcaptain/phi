using Phi.Agent;

namespace Phi.Tests;

public class FileOpsExtractorTests
{
    private static AssistantMessage AssistantWith(params ContentBlock[] blocks) => new()
    {
        Content = blocks,
        StopReason = StopReasons.ToolUse,
    };

    private static ToolCall Call(string name, string path) => new("id-" + name, name)
    {
        Arguments = new System.Text.Json.Nodes.JsonObject { ["path"] = path },
    };

    [Test]
    public async Task Extract_EmptyHistory_ReturnsEmpty()
    {
        var ops = FileOpsExtractor.Extract([]);
        await Assert.That(ops.ReadFiles).IsEmpty();
        await Assert.That(ops.ModifiedFiles).IsEmpty();
    }

    [Test]
    public async Task Extract_ReadToolCall_AddsToReadFiles()
    {
        var ops = FileOpsExtractor.Extract(
        [
            new AssistantMessage
            {
                Content = [Call("read", "src/a.ts")],
                StopReason = StopReasons.ToolUse,
            },
        ]);
        await Assert.That(ops.ReadFiles).IsEquivalentTo(["src/a.ts"]);
        await Assert.That(ops.ModifiedFiles).IsEmpty();
    }

    [Test]
    public async Task Extract_WriteAndEditToolCalls_AddsToModifiedFiles()
    {
        var ops = FileOpsExtractor.Extract(
        [
            new AssistantMessage
            {
                Content = [Call("write", "src/a.ts"), Call("edit", "src/b.ts")],
                StopReason = StopReasons.ToolUse,
            },
        ]);
        await Assert.That(ops.ModifiedFiles).IsEquivalentTo(["src/a.ts", "src/b.ts"]);
        await Assert.That(ops.ReadFiles).IsEmpty();
    }

    [Test]
    public async Task Extract_BashToolCall_Ignored()
    {
        var bash = new ToolCall("t1", "bash")
        {
            Arguments = new System.Text.Json.Nodes.JsonObject { ["command"] = "cat /etc/passwd" },
        };
        var ops = FileOpsExtractor.Extract(
        [
            new AssistantMessage { Content = [bash], StopReason = StopReasons.ToolUse },
        ]);
        await Assert.That(ops.ReadFiles).IsEmpty();
        await Assert.That(ops.ModifiedFiles).IsEmpty();
    }

    [Test]
    public async Task Extract_ToolCallMissingPath_Ignored()
    {
        var noPath = new ToolCall("t1", "read")
        {
            Arguments = [],
        };
        var ops = FileOpsExtractor.Extract(
        [
            new AssistantMessage { Content = [noPath], StopReason = StopReasons.ToolUse },
        ]);
        await Assert.That(ops.ReadFiles).IsEmpty();
    }

    [Test]
    public async Task Extract_DuplicatePaths_DeduplicatedPreservingOrder()
    {
        var ops = FileOpsExtractor.Extract(
        [
            new AssistantMessage
            {
                Content = [Call("read", "src/a.ts")],
                StopReason = StopReasons.ToolUse,
            },
            new AssistantMessage
            {
                Content = [Call("read", "src/a.ts"), Call("read", "src/b.ts")],
                StopReason = StopReasons.ToolUse,
            },
        ]);
        await Assert.That(ops.ReadFiles).IsEquivalentTo(["src/a.ts", "src/b.ts"]);
        await Assert.That(ops.ReadFiles.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Merge_CombinesBothListsDeduplicating()
    {
        var a = new CompactionDetails(
            ReadFiles: ["a.ts", "b.ts"],
            ModifiedFiles: ["c.ts"]);
        var b = new CompactionDetails(
            ReadFiles: ["b.ts", "d.ts"],
            ModifiedFiles: ["c.ts", "e.ts"]);
        var merged = a.Merge(b);
        await Assert.That(merged.ReadFiles).IsEquivalentTo(["a.ts", "b.ts", "d.ts"]);
        await Assert.That(merged.ModifiedFiles).IsEquivalentTo(["c.ts", "e.ts"]);
    }

    [Test]
    public async Task Merge_NullOther_ReturnsThisUnchanged()
    {
        var a = new CompactionDetails(["x"], ["y"]);
        var merged = a.Merge(null);
        await Assert.That(merged.ReadFiles).IsEquivalentTo(["x"]);
        await Assert.That(merged.ModifiedFiles).IsEquivalentTo(["y"]);
    }
}
