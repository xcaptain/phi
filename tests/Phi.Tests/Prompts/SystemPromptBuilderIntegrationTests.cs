using Phi.Agent;
using Phi.Prompts;
using Phi.Resources;

namespace Phi.Tests.Prompts;

public class SystemPromptBuilderIntegrationTests : IDisposable
{
    private readonly string _root;

    public SystemPromptBuilderIntegrationTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            $"phi-prompt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task EndToEnd_AgentsFileFlowsIntoPrompt()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var pkg = Path.Combine(_root, "pkg");
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), "ROOT-RULES");
        File.WriteAllText(Path.Combine(pkg, "AGENTS.md"), "PKG-RULES");

        var resources = ProjectContextLoader.Load(new SessionResourceOptions { Cwd = pkg });
        var prompt = new SystemPromptBuilder().Build(new SystemPromptBuildContext
        {
            Cwd = pkg,
            CurrentDate = new DateOnly(2026, 1, 1),
            Tools = [],
            Skills = [],
            ContextFiles = resources.ContextFiles,
            Options = new SystemPromptOptions(),
        });

        await Assert.That(prompt).Contains("ROOT-RULES");
        await Assert.That(prompt).Contains("PKG-RULES");
        var rootIdx = prompt.IndexOf("ROOT-RULES", StringComparison.Ordinal);
        var pkgIdx = prompt.IndexOf("PKG-RULES", StringComparison.Ordinal);
        await Assert.That(rootIdx).IsLessThan(pkgIdx);
    }

    [Test]
    public async Task EndToEnd_SkillFlowsIntoAvailableSkills_WhenReadToolPresent()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var skillDir = Path.Combine(_root, ".agents", "skills", "dotnet-testing");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"),
            "---\nname: dotnet-testing\ndescription: Write xUnit tests\n---\nbody\n");

        var skillResult = SkillLoader.Load(new SkillLoadOptions { Cwd = _root });
        var readContribution = new ToolContribution
        {
            Tool = new StubReadTool(),
            PromptSnippet = "read: Read a file.",
            Capabilities = ToolCapabilities.ReadLocalFiles,
        };

        var prompt = new SystemPromptBuilder().Build(new SystemPromptBuildContext
        {
            Cwd = _root,
            CurrentDate = new DateOnly(2026, 1, 1),
            Tools = [readContribution],
            Skills = skillResult.Skills,
            ContextFiles = [],
            Options = new SystemPromptOptions(),
        });

        await Assert.That(prompt).Contains("<available_skills>");
        await Assert.That(prompt).Contains("<name>dotnet-testing</name>");
        await Assert.That(prompt).Contains("Write xUnit tests");
    }

    private sealed class StubReadTool : Phi.Agent.Tool
    {
        public override string Name => "read";
        public override string Description => "Read a file.";
        public override System.Text.Json.Nodes.JsonObject Parameters =>
            new() { ["type"] = "object" };
        public override Task<Phi.Agent.ToolResult> ExecuteAsync(
            string toolName,
            string toolCallId,
            System.Text.Json.Nodes.JsonObject arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Phi.Agent.ToolResult(
                Content: [new Phi.Agent.TextBlock("ok")]));
    }
}
