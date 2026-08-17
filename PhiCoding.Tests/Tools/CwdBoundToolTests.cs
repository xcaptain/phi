using PhiCoding.Prompts;
using PhiCoding.Tools;

namespace PhiCoding.Tests.Tools;

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
        var text = string.Concat(result.Content.OfType<PhiAgent.TextBlock>().Select(b => b.Text));
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
        var text = string.Concat(result.Content.OfType<PhiAgent.TextBlock>().Select(b => b.Text));
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
        var text = string.Concat(result.Content.OfType<PhiAgent.TextBlock>().Select(b => b.Text));
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
    public async Task BuiltInToolProvider_BashToolHonoursCwd()
    {
        // /bin/bash does not exist on desktop Windows (the tool switches to
        // PowerShell there); CI runs on Linux, so just no-op the assertion.
        if (OperatingSystem.IsWindows())
            return;

        var provider = new BuiltInToolProvider(_cwd);
        PhiAgent.Tool bash = provider.GetTools().Single(c => c.Tool.Name == "bash").Tool;
        var args = new System.Text.Json.Nodes.JsonObject { ["command"] = "pwd" };

        var result = await bash.ExecuteAsync("bash", "c1", args, CancellationToken.None);

        await Assert.That(result.IsError).IsFalse();
        var text = string.Concat(result.Content.OfType<PhiAgent.TextBlock>().Select(b => b.Text));
        await Assert.That(text).Contains(_cwd);
    }

    [Test]
    public async Task BuiltInTools_CreateDefault_ProducesCwdBoundInstances()
    {
        var tools = BuiltInTools.CreateDefault(_cwd);

        await Assert.That(tools).Count().IsEqualTo(4);
        await Assert.That(tools.Select(t => t.Name)).IsEquivalentTo(["bash", "read", "write", "edit"]);
    }

    [Test]
    public async Task ToolComposer_DuplicateName_Throws()
    {
        var first = new ToolContribution
        {
            Tool = new PromptTestTool("read", "first"),
            Source = "builtin",
        };
        var dup = new ToolContribution
        {
            Tool = new PromptTestTool("read", "second"),
            Source = "custom",
        };

        var ex = await Assert.That(async () =>
            await ToolComposer.ComposeAsync([first, dup]))
            .Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("read");
    }

    [Test]
    public async Task ToolComposer_UniqueNames_PassesThrough()
    {
        var first = new ToolContribution
        {
            Tool = new PromptTestTool("read", "first"),
            Source = "builtin",
        };
        var second = new ToolContribution
        {
            Tool = new PromptTestTool("bash", "second"),
            Source = "builtin",
        };

        var composed = await ToolComposer.ComposeAsync([first, second]);

        await Assert.That(composed.Select(c => c.Tool.Name)).IsEquivalentTo(["read", "bash"]);
    }

    private sealed class PromptTestTool(string name, string description) : PhiAgent.Tool
    {
        public override string Name { get; } = name;
        public override string Description { get; } = description;
        public override System.Text.Json.Nodes.JsonObject Parameters =>
            new() { ["type"] = "object" };
        public override Task<PhiAgent.ToolResult> ExecuteAsync(
            string toolName,
            string toolCallId,
            System.Text.Json.Nodes.JsonObject arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PhiAgent.ToolResult(
                Content: [new PhiAgent.TextBlock("ok")]));
    }
}
