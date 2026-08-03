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