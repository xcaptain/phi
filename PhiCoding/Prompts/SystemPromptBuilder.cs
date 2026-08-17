using System.Globalization;
using System.Text;

namespace PhiCoding.Prompts;

/// <summary>
/// Default <see cref="ISystemPromptBuilder"/>. Renders sections in this
/// order: base identity, available tools, guidelines,
/// <see cref="SystemPromptOptions.AppendSystemPrompt"/>,
/// <c>&lt;project_context&gt;</c>, <c>&lt;available_skills&gt;</c>, date,
/// then the working directory.
///
/// The builder is pure: no file IO, no time-of-day reads, no globals. All
/// inputs flow through <see cref="SystemPromptBuildContext"/>.
/// </summary>
public sealed class SystemPromptBuilder : ISystemPromptBuilder
{
    private const string DefaultBasePrompt =
        "You are an expert coding assistant operating inside Phi, a coding-agent harness.\n" +
        "You help users by reading and editing files and running shell commands.";

    public string Build(SystemPromptBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Options.ResolvedSystemPrompt is { } resolved)
            return resolved;

        var sb = new StringBuilder();
        AppendBase(sb, context);
        AppendEnvironment(sb, context);
        AppendAppend(sb, context);
        AppendProjectContext(sb, context);
        AppendAvailableSkills(sb, context);
        AppendDate(sb, context);
        AppendCwd(sb, context);
        return sb.ToString();
    }

    private static void AppendBase(StringBuilder sb, SystemPromptBuildContext ctx)
    {
        var baseText = ctx.Options.CustomBasePrompt ?? DefaultBasePrompt;
        sb.AppendLine(baseText);
        sb.AppendLine();
        AppendAvailableToolsSection(sb, ctx);
        AppendGuidelinesSection(sb, ctx);
    }

    private static void AppendAvailableToolsSection(StringBuilder sb, SystemPromptBuildContext ctx)
    {
        sb.AppendLine("## Available tools");
        sb.AppendLine();
        if (ctx.Tools.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var t in ctx.Tools
                .OrderBy(t => t.Source, StringComparer.Ordinal)
                .ThenBy(t => t.Tool.Name, StringComparer.Ordinal))
            {
                sb.Append("- ").Append(t.Tool.Name).Append(": ");
                sb.AppendLine(t.PromptSnippet ?? t.Tool.Description);
            }
        }
        sb.AppendLine();
    }

    private static void AppendGuidelinesSection(StringBuilder sb, SystemPromptBuildContext ctx)
    {
        sb.AppendLine("## Guidelines");
        sb.AppendLine();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in ctx.Tools)
        {
            foreach (var line in tool.PromptGuidelines)
            {
                if (!string.IsNullOrWhiteSpace(line) && seen.Add(line))
                    sb.Append("- ").AppendLine(line);
            }
        }
        sb.AppendLine("- Be concise.");
        sb.AppendLine();
    }

    /// <summary>
    /// Declares the runtime shell so the model emits the right syntax. The
    /// tool is still named <c>bash</c> everywhere (stable schema), but on
    /// desktop Windows it actually executes PowerShell — the model must be
    /// told that explicitly or it will keep writing bash.
    /// </summary>
    private static void AppendEnvironment(StringBuilder sb, SystemPromptBuildContext ctx)
    {
        sb.AppendLine("## Environment");
        sb.AppendLine();
        if (ctx.Shell == ShellKind.PowerShell)
        {
            sb.AppendLine("You are running on Windows. The bash tool executes commands in PowerShell (pwsh 7 when installed, otherwise Windows PowerShell 5.1).");
            sb.AppendLine("Use PowerShell syntax rather than bash:");
            sb.AppendLine("- Get-ChildItem instead of ls, Get-Content instead of cat.");
            sb.AppendLine("- $env:NAME reads an environment variable.");
            sb.AppendLine("- Join commands with ';' or a new line instead of '&&'.");
        }
        else
        {
            sb.AppendLine("Shell: bash.");
        }
        sb.AppendLine();
    }

    private static void AppendAppend(StringBuilder sb, SystemPromptBuildContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.Options.AppendSystemPrompt))
            return;
        sb.AppendLine(ctx.Options.AppendSystemPrompt);
        sb.AppendLine();
    }

    private static void AppendProjectContext(StringBuilder sb, SystemPromptBuildContext ctx)
    {
        if (ctx.ContextFiles.Count == 0)
            return;
        sb.AppendLine("<project_context>");
        foreach (var file in ctx.ContextFiles)
        {
            sb.Append("  <project_instructions path=\"")
              .Append(file.AbsolutePath)
              .AppendLine("\">");
            sb.AppendLine(file.Content.TrimEnd());
            sb.AppendLine("  </project_instructions>");
        }
        sb.AppendLine("</project_context>");
        sb.AppendLine();
    }

    private static void AppendAvailableSkills(StringBuilder sb, SystemPromptBuildContext ctx)
    {
        if (ctx.Skills.Count == 0)
            return;
        var canRead = ctx.Tools.Any(t =>
            t.Capabilities.HasFlag(ToolCapabilities.ReadLocalFiles));
        if (!canRead)
            return;
        sb.AppendLine("The following skills provide specialized instructions for specific tasks.");
        sb.AppendLine("Use the read tool to load a skill's file when the task matches its description.");
        sb.AppendLine("When a skill file references a relative path, resolve it against the skill directory and use that absolute path in tool commands.");
        sb.AppendLine();
        sb.AppendLine("<available_skills>");
        foreach (var skill in ctx.Skills)
        {
            sb.AppendLine("  <skill>");
            sb.Append("    <name>").Append(skill.Name).AppendLine("</name>");
            sb.Append("    <description>").Append(skill.Description).AppendLine("</description>");
            sb.Append("    <location>").Append(skill.AbsolutePath).AppendLine("</location>");
            sb.AppendLine("  </skill>");
        }
        sb.AppendLine("</available_skills>");
        sb.AppendLine();
    }

    private static void AppendDate(StringBuilder sb, SystemPromptBuildContext ctx)
    {
        sb.AppendLine("## Date");
        sb.AppendLine();
        sb.AppendLine(ctx.CurrentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        sb.AppendLine();
    }

    private static void AppendCwd(StringBuilder sb, SystemPromptBuildContext ctx)
    {
        sb.AppendLine("## Working directory");
        sb.AppendLine();
        sb.AppendLine(ctx.Cwd);
    }
}
