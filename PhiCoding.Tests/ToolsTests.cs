using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

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
        var args = JsonNode.Parse($$"""{"path":"{{file.Path}}"}""")!;

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
        var args = JsonNode.Parse($$"""{"path":"{{missing}}"}""")!;

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
            var args = JsonNode.Parse($$"""{"path":"{{dir}}"}""")!;
            var result = await tool.ExecuteAsync("c1", args, default);

            await Assert.That(result.IsError).IsTrue();
            await Assert.That(result.Text).Contains("directory");
        }
        finally
        {
            Directory.Delete(dir);
        }
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
            var args = JsonNode.Parse($$"""{"path":"{{path}}","content":"new content"}""")!;

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
            var args = JsonNode.Parse($$"""{"path":"{{path}}","content":"deep"}""")!;

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
        var args = JsonNode.Parse($$"""{"path":"{{file.Path}}","content":"new content"}""")!;

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
        var args = JsonNode.Parse($$"""
            {"path":"{{file.Path}}","oldString":"foo bar","newString":"FOO BAR"}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("hello world\nFOO BAR\nbaz");
    }

    [Test]
    public async Task ExecuteAsync_OldStringNotFound_ReturnsError()
    {
        using var file = new TempFile("hello world");
        var tool = new EditTool();
        var args = JsonNode.Parse($$"""
            {"path":"{{file.Path}}","oldString":"nonexistent","newString":"X"}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("not found");
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("hello world");
    }

    [Test]
    public async Task ExecuteAsync_OldStringAppearsMultipleTimes_ReturnsError()
    {
        using var file = new TempFile("foo foo foo");
        var tool = new EditTool();
        var args = JsonNode.Parse($$"""
            {"path":"{{file.Path}}","oldString":"foo","newString":"bar"}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("must be unique");
        await Assert.That(File.ReadAllText(file.Path)).IsEqualTo("foo foo foo");
    }

    [Test]
    public async Task ExecuteAsync_MissingFile_ReturnsError()
    {
        var missing = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"phi-edit-missing-{Guid.NewGuid():N}");
        var tool = new EditTool();
        var args = JsonNode.Parse($$"""
            {"path":"{{missing}}","oldString":"x","newString":"y"}
            """)!;

        var result = await tool.ExecuteAsync("c1", args, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("File not found");
    }
}