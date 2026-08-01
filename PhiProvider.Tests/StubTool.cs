using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiProvider.Tests;

/// <summary>
/// A minimal concrete <see cref="Tool"/> for provider tests: carries the
/// schema (name / description / parameters) and a no-op executor, mirroring
/// the former <c>new Tool(name, desc, parameters)</c> record construction.
/// </summary>
public sealed class StubTool : Tool
{
    private readonly string _name;
    private readonly string _description;
    private readonly JsonObject _parameters;

    public StubTool(string Name, string Description, JsonObject Parameters)
    {
        _name = Name;
        _description = Description;
        _parameters = Parameters;
    }

    public override string Name => _name;
    public override string Description => _description;
    public override JsonObject Parameters => _parameters;

    public override Task<ToolResult> ExecuteAsync(
        string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
        => Task.FromResult(new ToolResult([new TextBlock("stub")]));
}
