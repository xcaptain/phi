using System.Text.Json.Nodes;

namespace PhiAgent.Tests;

/// <summary>
/// A <see cref="Tool"/> backed by a delegate, for tests that need to inject
/// a specific execution behavior. The name it is registered under must
/// match the tool call name the loop looks up.
/// </summary>
public sealed class FuncTool : Tool
{
    private readonly Func<string, string, JsonObject, CancellationToken, Task<ToolResult>> _execute;

    public FuncTool(
        string name,
        Func<string, string, JsonObject, CancellationToken, Task<ToolResult>> execute,
        string description = "")
    {
        Name = name;
        Description = description;
        Parameters = new JsonObject { ["type"] = "object" };
        _execute = execute;
    }

    public override string Name { get; }
    public override string Description { get; }
    public override JsonObject Parameters { get; }

    public override Task<ToolResult> ExecuteAsync(
        string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
        => _execute(toolName, toolCallId, arguments, cancellationToken);
}
