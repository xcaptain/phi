using PhiCoding.Prompts;

namespace PhiCoding.Tests.Prompts;

public class SystemPromptBuilderTests
{
    private static SystemPromptBuildContext BuildContext(
        SystemPromptOptions? options = null,
        IReadOnlyList<ToolContribution>? tools = null,
        IReadOnlyList<SkillDescriptor>? skills = null,
        IReadOnlyList<ProjectContextFile>? contextFiles = null,
        string cwd = "/work",
        DateOnly? date = null) =>
        new()
        {
            Cwd = cwd,
            CurrentDate = date ?? new DateOnly(2026, 1, 15),
            Tools = tools ?? [],
            Skills = skills ?? [],
            ContextFiles = contextFiles ?? [],
            Options = options ?? new SystemPromptOptions(),
        };

    private static ToolContribution ReadToolContribution() =>
        new()
        {
            Tool = new PromptTestTool("read", "Read a file from the local workspace."),
            PromptSnippet = "read: Read a file from the local workspace.",
            PromptGuidelines = ["Use read to inspect files before editing them."],
            Capabilities = ToolCapabilities.ReadLocalFiles,
            Source = "builtin",
        };

    [Test]
    public async Task ResolvedSystemPrompt_ShortCircuitsBuilder()
    {
        var builder = new SystemPromptBuilder();
        var prompt = builder.Build(BuildContext(
            options: new SystemPromptOptions { ResolvedSystemPrompt = "final" }));

        await Assert.That(prompt).IsEqualTo("final");
    }

    [Test]
    public async Task Default_ContainsIdentityAndAvailableToolsHeader()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext());

        await Assert.That(prompt).Contains("Phi");
        await Assert.That(prompt).Contains("Available tools");
    }

    [Test]
    public async Task Default_WithNoTools_OmitsToolEntriesButKeepsHeader()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext(tools: []));

        await Assert.That(prompt).Contains("Available tools");
        await Assert.That(prompt).Contains("(none)");
    }

    [Test]
    public async Task Default_RendersToolSnippetFromContribution()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext(tools: [ReadToolContribution()]));

        await Assert.That(prompt).Contains("read: Read a file from the local workspace.");
    }

    [Test]
    public async Task Default_FallsBackToToolDescriptionWhenSnippetMissing()
    {
        var builder = new SystemPromptBuilder();
        var contribution = ReadToolContribution() with { PromptSnippet = null };

        var prompt = builder.Build(BuildContext(tools: [contribution]));

        await Assert.That(prompt).Contains("read: Read a file from the local workspace.");
    }

    [Test]
    public async Task Default_IncludesToolGuidelines()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext(tools: [ReadToolContribution()]));

        await Assert.That(prompt).Contains("Use read to inspect files before editing them.");
    }

    [Test]
    public async Task Default_Guidelines_AreDeduplicatedByFirstOccurrence()
    {
        var builder = new SystemPromptBuilder();
        var tool = new PromptTestTool("read", "Read");
        var shared = "Do not use cat/sed/head to read files.";
        var first = new ToolContribution
        {
            Tool = tool,
            PromptSnippet = "read",
            PromptGuidelines = [shared, "Unique guideline A"],
            Capabilities = ToolCapabilities.ReadLocalFiles,
        };
        var second = new ToolContribution
        {
            Tool = new PromptTestTool("bash", "Run shell"),
            PromptSnippet = "bash",
            PromptGuidelines = [shared, "Unique guideline B"],
            Capabilities = ToolCapabilities.ExecuteCommands,
        };

        var prompt = builder.Build(BuildContext(tools: [first, second]));

        var firstIndex = prompt.IndexOf(shared, StringComparison.Ordinal);
        var uniqueA = prompt.IndexOf("Unique guideline A", StringComparison.Ordinal);
        var uniqueB = prompt.IndexOf("Unique guideline B", StringComparison.Ordinal);
        await Assert.That(firstIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(uniqueA).IsGreaterThan(firstIndex);
        await Assert.That(uniqueB).IsGreaterThan(firstIndex);
        await Assert.That(prompt.IndexOf(shared, firstIndex + 1, StringComparison.Ordinal)).IsEqualTo(-1);
    }

    [Test]
    public async Task CustomBasePrompt_Null_UsesDefaultBase()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext(options: new SystemPromptOptions
        {
            CustomBasePrompt = null,
        }));

        await Assert.That(prompt).Contains("Phi");
    }

    [Test]
    public async Task CustomBasePrompt_Empty_ReplacesDefaultBase()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext(
            options: new SystemPromptOptions { CustomBasePrompt = "" },
            tools: [ReadToolContribution()]));

        await Assert.That(prompt).DoesNotContain("Phi");
        await Assert.That(prompt).Contains("Available tools");
    }

    [Test]
    public async Task CustomBasePrompt_Text_ReplacesDefaultBase()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext(
            options: new SystemPromptOptions { CustomBasePrompt = "I am a custom agent." },
            tools: [ReadToolContribution()]));

        await Assert.That(prompt).Contains("I am a custom agent.");
        await Assert.That(prompt).DoesNotContain("expert coding assistant");
        await Assert.That(prompt).Contains("Available tools");
    }

    [Test]
    public async Task AppendSystemPrompt_GoesAfterBase()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext(options: new SystemPromptOptions
        {
            AppendSystemPrompt = "Always answer in haiku.",
        }));

        var baseIdx = prompt.IndexOf("Phi", StringComparison.Ordinal);
        var appendIdx = prompt.IndexOf("Always answer in haiku.", StringComparison.Ordinal);
        await Assert.That(baseIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(appendIdx).IsGreaterThan(baseIdx);
    }

    [Test]
    public async Task AppendSystemPrompt_GoesAfterCustomBase()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext(options: new SystemPromptOptions
        {
            CustomBasePrompt = "CUSTOM-BASE",
            AppendSystemPrompt = "APPEND-TAIL",
        }));

        await Assert.That(prompt.IndexOf("CUSTOM-BASE", StringComparison.Ordinal))
            .IsLessThan(prompt.IndexOf("APPEND-TAIL", StringComparison.Ordinal));
    }

    [Test]
    public async Task Environment_DefaultBash_StatesBash()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext());

        await Assert.That(prompt).Contains("## Environment");
        await Assert.That(prompt).Contains("Shell: bash.");
        await Assert.That(prompt).DoesNotContain("PowerShell");
    }

    [Test]
    public async Task Environment_PowerShell_GuidesPowerShellSyntax()
    {
        var builder = new SystemPromptBuilder();
        var ctx = BuildContext() with { Shell = ShellKind.PowerShell };

        var prompt = builder.Build(ctx);

        await Assert.That(prompt).Contains("## Environment");
        await Assert.That(prompt).Contains("running on Windows");
        await Assert.That(prompt).Contains("Get-ChildItem");
        await Assert.That(prompt).Contains("Get-Content");
        await Assert.That(prompt).Contains("$env:");
        await Assert.That(prompt).DoesNotContain("Shell: bash.");
    }

    [Test]
    public async Task ProjectContext_IsWrappedAndAppearsAfterBase()
    {
        var builder = new SystemPromptBuilder();
        var ctx = new ProjectContextFile("/abs/AGENTS.md", "Be terse.");

        var prompt = builder.Build(BuildContext(
            contextFiles: [ctx],
            options: new SystemPromptOptions()));

        var contextIdx = prompt.IndexOf("<project_context>", StringComparison.Ordinal);
        var identityIdx = prompt.IndexOf("Phi", StringComparison.Ordinal);
        await Assert.That(contextIdx).IsGreaterThan(identityIdx);
        await Assert.That(prompt).Contains("<project_instructions path=\"/abs/AGENTS.md\">");
        await Assert.That(prompt).Contains("Be terse.");
    }

    [Test]
    public async Task Skills_AppearOnlyWhenReadToolPresent()
    {
        var builder = new SystemPromptBuilder();
        var skill = new SkillDescriptor
        {
            Name = "dotnet",
            Description = "C# guidance",
            AbsolutePath = "/abs/dotnet/SKILL.md",
        };
        var toolsWithoutRead = new ToolContribution
        {
            Tool = new PromptTestTool("bash", "shell"),
            PromptSnippet = "bash",
            Capabilities = ToolCapabilities.ExecuteCommands,
        };

        var without = builder.Build(BuildContext(
            tools: [toolsWithoutRead],
            skills: [skill]));
        await Assert.That(without).DoesNotContain("<available_skills>");

        var with = builder.Build(BuildContext(
            tools: [ReadToolContribution()],
            skills: [skill]));
        await Assert.That(with).Contains("<available_skills>");
        await Assert.That(with).Contains("<name>dotnet</name>");
        await Assert.That(with).Contains("<location>/abs/dotnet/SKILL.md</location>");
        await Assert.That(with).DoesNotContain("C# guidance\n\n"); // description stays inline
    }

    [Test]
    public async Task Skills_NotPresent_OmitsBlock()
    {
        var builder = new SystemPromptBuilder();

        var prompt = builder.Build(BuildContext(tools: [ReadToolContribution()]));

        await Assert.That(prompt).DoesNotContain("<available_skills>");
    }

    [Test]
    public async Task Skills_InstructionsExplainProgressiveDisclosureAndRelativePaths()
    {
        var builder = new SystemPromptBuilder();
        var skill = new SkillDescriptor
        {
            Name = "dotnet",
            Description = "C# guidance",
            AbsolutePath = "/abs/dotnet/SKILL.md",
        };

        var prompt = builder.Build(BuildContext(
            tools: [ReadToolContribution()],
            skills: [skill]));

        await Assert.That(prompt).Contains("The following skills provide specialized instructions for specific tasks.");
        await Assert.That(prompt).Contains("Use the read tool to load a skill's file when the task matches its description.");
        await Assert.That(prompt).Contains("resolve it against the skill directory");

        var instructionsIdx = prompt.IndexOf(
            "The following skills provide", StringComparison.Ordinal);
        var blockIdx = prompt.IndexOf("<available_skills>", StringComparison.Ordinal);
        await Assert.That(instructionsIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(blockIdx).IsGreaterThan(instructionsIdx);
    }

    [Test]
    public async Task Skills_InstructionsGatedBehindReadTool_WithNoReadTool()
    {
        var builder = new SystemPromptBuilder();
        var skill = new SkillDescriptor
        {
            Name = "dotnet",
            Description = "C# guidance",
            AbsolutePath = "/abs/dotnet/SKILL.md",
        };
        var noRead = new ToolContribution
        {
            Tool = new PromptTestTool("bash", "shell"),
            PromptSnippet = "bash",
            Capabilities = ToolCapabilities.ExecuteCommands,
        };

        var prompt = builder.Build(BuildContext(tools: [noRead], skills: [skill]));

        await Assert.That(prompt).DoesNotContain("The following skills provide specialized instructions");
        await Assert.That(prompt).DoesNotContain("<available_skills>");
    }

    [Test]
    public async Task DateAndCwd_AppearAtEnd()
    {
        var builder = new SystemPromptBuilder();
        var date = new DateOnly(2026, 7, 4);

        var prompt = builder.Build(BuildContext(
            cwd: "/work/phi",
            date: date,
            tools: [ReadToolContribution()]));

        await Assert.That(prompt).Contains("2026-07-04");
        await Assert.That(prompt).Contains("/work/phi");

        var dateHeaderIdx = prompt.IndexOf("## Date", StringComparison.Ordinal);
        var cwdHeaderIdx = prompt.IndexOf("## Working directory", StringComparison.Ordinal);
        var dateIdx = prompt.IndexOf("2026-07-04", StringComparison.Ordinal);
        var cwdIdx = prompt.IndexOf("/work/phi", StringComparison.Ordinal);

        await Assert.That(dateHeaderIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(cwdHeaderIdx).IsGreaterThan(dateHeaderIdx);
        await Assert.That(dateIdx).IsGreaterThan(dateHeaderIdx);
        await Assert.That(cwdIdx).IsGreaterThan(cwdHeaderIdx);
    }

    [Test]
    public async Task Builder_IsDeterministicForSameContext()
    {
        var builder = new SystemPromptBuilder();
        var ctx = BuildContext(tools: [ReadToolContribution()]);

        var first = builder.Build(ctx);
        var second = builder.Build(ctx);

        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task ResolvedSystemPrompt_WinsOverEverythingElse()
    {
        var builder = new SystemPromptBuilder();
        var ctx = new SystemPromptBuildContext
        {
            Cwd = "/work",
            CurrentDate = new DateOnly(2026, 1, 1),
            Tools = [ReadToolContribution()],
            Skills = [new SkillDescriptor { Name = "foo", Description = "bar", AbsolutePath = "/abs/foo/SKILL.md" }],
            ContextFiles = [new ProjectContextFile("/abs/AGENTS.md", "no")],
            Options = new SystemPromptOptions
            {
                ResolvedSystemPrompt = "literal",
                CustomBasePrompt = "ignored",
                AppendSystemPrompt = "ignored",
            },
        };

        var prompt = builder.Build(ctx);

        await Assert.That(prompt).IsEqualTo("literal");
    }
}

internal sealed class PromptTestTool(string name, string description) : PhiAgent.Tool
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
