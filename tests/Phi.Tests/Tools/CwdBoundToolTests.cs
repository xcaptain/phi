using Phi.Extensions.CodingPack.Tools;

namespace Phi.Tests.Tools;

public class CwdBoundToolTests : IDisposable
{
    private readonly string _cwd;
    private readonly WorkspacePathResolver _resolver;

    public CwdBoundToolTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), $"phi-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cwd);
        _resolver = new WorkspacePathResolver(_cwd);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cwd))
            Directory.Delete(_cwd, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task BashTool_RunsInProvidedCwd()
    {
        // /bin/bash does not exist on desktop Windows (the tool switches to
        // PowerShell there); CI runs on Linux, so just no-op the assertion.
        if (OperatingSystem.IsWindows())
            return;

        var tool = new BashTool(_cwd);
        var args = new System.Text.Json.Nodes.JsonObject { ["command"] = "pwd" };

        var result = await tool.ExecuteAsync("c1", args, CancellationToken.None);

        await Assert.That(result.IsError).IsFalse();
        var text = string.Concat(result.Content.OfType<Phi.Agent.TextBlock>().Select(b => b.Text));
        await Assert.That(text).Contains(_cwd);
    }

    [Test]
    public async Task ReadTool_RelativePath_ResolvesAgainstCwd()
    {
        File.WriteAllText(Path.Combine(_cwd, "note.txt"), "hello\nworld");
        var tool = new ReadTool(_resolver);
        var args = new System.Text.Json.Nodes.JsonObject { ["path"] = "note.txt" };

        var result = await tool.ExecuteAsync("c1", args, CancellationToken.None);

        await Assert.That(result.IsError).IsFalse();
        var text = string.Concat(result.Content.OfType<Phi.Agent.TextBlock>().Select(b => b.Text));
        await Assert.That(text).Contains("hello");
    }

    [Test]
    public async Task ReadTool_AbsolutePath_StillWorks()
    {
        var absolute = Path.Combine(_cwd, "abs.txt");
        File.WriteAllText(absolute, "abs-content");
        var tool = new ReadTool(_resolver);
        var args = new System.Text.Json.Nodes.JsonObject { ["path"] = absolute };

        var result = await tool.ExecuteAsync("c1", args, CancellationToken.None);

        await Assert.That(result.IsError).IsFalse();
        var text = string.Concat(result.Content.OfType<Phi.Agent.TextBlock>().Select(b => b.Text));
        await Assert.That(text).IsEqualTo("abs-content");
    }

    [Test]
    public async Task WriteTool_RelativePath_WritesInsideCwd()
    {
        var tool = new WriteTool(_resolver);
        var args = new System.Text.Json.Nodes.JsonObject
        {
            ["path"] = "sub/out.txt",
            ["content"] = "written",
        };

        var result = await tool.ExecuteAsync("c1", args, CancellationToken.None);

        await Assert.That(result.IsError).IsFalse();
        var written = File.ReadAllText(Path.Combine(_cwd, "sub", "out.txt"));
        await Assert.That(written).IsEqualTo("written");
    }

    [Test]
    public async Task EditTool_InsideCwd_AppliesEdit()
    {
        File.WriteAllText(Path.Combine(_cwd, "f.txt"), "hello world");
        var tool = new EditTool(_resolver);
        var args = new System.Text.Json.Nodes.JsonObject
        {
            ["path"] = "f.txt",
            ["edits"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["oldText"] = "hello",
                    ["newText"] = "bye",
                },
            },
        };

        var result = await tool.ExecuteAsync("c1", args, CancellationToken.None);

        await Assert.That(result.IsError).IsFalse();
        var content = File.ReadAllText(Path.Combine(_cwd, "f.txt"));
        await Assert.That(content).IsEqualTo("bye world");
    }

    [Test]
    public async Task CodingPack_ProvidesTheFourDefaultTools()
    {
        // Sprint 2.5: the four default coding tools come from the CodingPack
        // extension now. Verify they instantiate cwd-bound.
        var tools = new Phi.Agent.Tool[]
        {
            new BashTool(_cwd),
            new ReadTool(_cwd),
            new WriteTool(_cwd),
            new EditTool(_cwd),
        };

        await Assert.That(tools).Count().IsEqualTo(4);
        await Assert.That(tools.Select(t => t.Name)).IsEquivalentTo(["bash", "read", "write", "edit"]);
    }
}
