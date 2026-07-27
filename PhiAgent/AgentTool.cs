using System.Text.Json.Nodes;

namespace PhiAgent;

/// <summary>
/// The provider's view of a tool: just what it needs to describe the tool on
/// the wire. Execution, rendering, and cancellation live in the agent layer.
/// </summary>
public sealed record AgentTool(
    string Name,
    string Description,
    IReadOnlyDictionary<string, JsonNode> Parameters);