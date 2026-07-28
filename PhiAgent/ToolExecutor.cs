using System.Text.Json.Nodes;

namespace PhiAgent;

public delegate Task<ToolResult> ToolExecutor(
    string toolName,
    string toolCallId,
    JsonNode arguments,
    CancellationToken cancellationToken = default);
