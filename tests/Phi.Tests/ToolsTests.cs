using System.Text.Json.Nodes;
using Phi.Extensions.CodingPack.Tools;

namespace Phi.Tests;

internal sealed class TempFile : IDisposable
{
    public string Path { get; }

    public TempFile(string content)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"phi-test-{Guid.NewGuid():N}");
        File.WriteAllText(Path, content);
    }

    public void Dispose()
    {
        if (File.Exists(Path)) File.Delete(Path);
    }
}

public class ReadToolTests
{
    [Test]
    public async Task ExecuteAsync_ExistingFile_ReturnsContent()
    {
        using var file = new TempFile("hello world\nline 2");
        var tool = new ReadTool();
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{file.Path}}"}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.Text).IsEqualTo("hello world\nline 2");
    }

    [Test]
    public async Task ExecuteAsync_MissingFile_ReturnsError()
    {
        var tool = new ReadTool();
        var missing = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"phi-missing-{Guid.NewGuid():N}");
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{missing}}"}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("File not found");
    }

    [Test]
    public async Task ExecuteAsync_PathIsDirectory_ReturnsError()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"phi-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var tool = new ReadTool();
            var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{dir}}"}""")!;
            var result = await tool.ExecuteAsync("c1", args, default);

            await Assert.That(result.IsError).IsTrue();
            await Assert.That(result.Text).Contains("directory");
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    private static List<string> LinesOf(string content) =>
        [.. content.Replace("\r\n", "\n").Split('\n')];

    [Test]
    public async Task ExecuteAsync_OffsetAndLimit_ReadsSlice()
    {
        var content = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        using var file = new TempFile(content);
        var tool = new ReadTool();
        var args = (JsonObject)JsonNode.Parse(
            $$"""{"path":"{{file.Path}}","offset":10,"limit":5}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        // Split off the trailing continuation hint (added when more lines
        // remain) so we can assert the actual file lines cleanly.
        var body = result.Text.Split("\n\n", 2)[0];
        await Assert.That(LinesOf(body))
            .IsEquivalentTo(["line 10", "line 11", "line 12", "line 13", "line 14"]);
        // The continuation hint must point at the next offset.
        await Assert.That(result.Text).Contains("offset=15");
        await Assert.That(result.Text).Contains("6 more lines in file");
    }

    [Test]
    public async Task ExecuteAsync_OffsetNull_StartsAtFirstLine()
    {
        var content = "a\nb\nc";
        using var file = new TempFile(content);
        var tool = new ReadTool();
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{file.Path}}","limit":2}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.Text).StartsWith("a\nb");
        await Assert.That(result.Text).Contains("offset=3");
    }

    [Test]
    public async Task ExecuteAsync_OffsetBeyondEnd_ReturnsError()
    {
        var content = "a\nb\nc";
        using var file = new TempFile(content);
        var tool = new ReadTool();
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{file.Path}}","offset":99}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("beyond end of file");
    }

    [Test]
    public async Task ExecuteAsync_LimitGreaterThanFile_ClampsToRemaining()
    {
        var content = "a\nb\nc";
        using var file = new TempFile(content);
        var tool = new ReadTool();
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{file.Path}}","offset":2,"limit":999}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        // Returns b + c, no continuation hint because we hit EOF.
        await Assert.That(result.Text).IsEqualTo("b\nc");
        await Assert.That(result.Text).DoesNotContain("more lines in file");
    }

    [Test]
    public async Task ExecuteAsync_OffsetLessThanOne_ReturnsError()
    {
        using var file = new TempFile("x");
        var tool = new ReadTool();
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{file.Path}}","offset":0}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("offset must be at least 1");
    }
}

public class WriteToolTests
{
    [Test]
    public async Task ExecuteAsync_NewFile_CreatesFileWithContent()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"phi-write-{Guid.NewGuid():N}.txt");
        try
        {
            var tool = new WriteTool();
            var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{path}}","content":"new content"}""")!;

            var result = await tool.ExecuteAsync("c1", args, default);

            await Assert.That(result.IsError).IsFalse();
            await Assert.That(File.Exists(path)).IsTrue();
            await Assert.That(File.ReadAllText(path)).IsEqualTo("new content");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public async Task ExecuteAsync_NestedPath_CreatesParentDirectories()
    {
        var baseDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"phi-nested-{Guid.NewGuid():N}");
        var path = System.IO.Path.Combine(baseDir, "sub", "deep", "file.txt");
        try
        {
            var tool = new WriteTool();
            var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{path}}","content":"deep"}""")!;

            var result = await tool.ExecuteAsync("c1", args, default);

            await Assert.That(result.IsError).IsFalse();
            await Assert.That(File.Exists(path)).IsTrue();
            await Assert.That(File.ReadAllText(path)).IsEqualTo("deep");
        }
        finally
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, recursive: true);
        }
    }

    [Test]
    public async Task ExecuteAsync_ExistingFile_OverwritesContent()
    {
        using var file = new TempFile("old content");
        var tool = new WriteTool();
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{file.Path}}","content":"new content"}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("new content");
    }
}

public class EditToolTests
{
    [Test]
    public async Task ExecuteAsync_UniqueMatch_ReplacesText()
    {
        using var file = new TempFile("hello world\nfoo bar\nbaz");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{file.Path}}","edits":[{"oldText":"foo bar","newText":"FOO BAR"}]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("hello world\nFOO BAR\nbaz");
    }

    [Test]
    public async Task ExecuteAsync_OldTextNotFound_ReturnsError()
    {
        using var file = new TempFile("hello world");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{file.Path}}","edits":[{"oldText":"nonexistent","newText":"X"}]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("not found");
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("hello world");
    }

    [Test]
    public async Task ExecuteAsync_OldTextAppearsMultipleTimes_ReturnsError()
    {
        using var file = new TempFile("foo foo foo");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{file.Path}}","edits":[{"oldText":"foo","newText":"bar"}]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("must be unique");
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("foo foo foo");
    }

    [Test]
    public async Task ExecuteAsync_EmptyEditsArray_ReturnsError()
    {
        using var file = new TempFile("hello");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""{"path":"{{file.Path}}","edits":[]}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("at least one");
    }

    [Test]
    public async Task ExecuteAsync_MultipleEdits_AppliesAll()
    {
        using var file = new TempFile("one\ntwo\nthree\nfour");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{file.Path}}","edits":[
                {"oldText":"one","newText":"1"},
                {"oldText":"four","newText":"4"}
            ]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("1\ntwo\nthree\n4");
    }

    [Test]
    public async Task ExecuteAsync_MissingFile_ReturnsError()
    {
        var missing = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"phi-edit-missing-{Guid.NewGuid():N}");
        var tool = new EditTool();
        var args = (JsonObject)JsonNode.Parse($$"""
            {"path":"{{missing}}","edits":[{"oldText":"x","newText":"y"}]}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("File not found");
    }
}

public class TypedToolTests
{
    [Test]
    public async Task ExecuteAsync_WrongArgumentType_ReturnsValidationError()
    {
        var tool = new ReadTool();
        var args = (JsonObject)JsonNode.Parse("""{"path":123}""")!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("validation error");
        await Assert.That(result.Text).Contains("$.path");
    }
}
