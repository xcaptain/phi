using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Rich tool definition with prompt-engineering metadata. Application-level
/// type; convert to the slim <see cref="Tool"/> via <see cref="ToTool"/> for
/// the agent loop. Mirrors tau's <c>ToolDefinition</c>.
/// </summary>
public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonObject Parameters,
    string? PromptSnippet = null,
    IReadOnlyList<string>? PromptGuidelines = null)
{
    public Tool ToTool() => new(Name, Description, Parameters);
}