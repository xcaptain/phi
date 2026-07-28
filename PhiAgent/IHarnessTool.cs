using System.Text.Json.Nodes;

namespace PhiAgent;

public interface IHarnessTool
{
    Tool Tool { get; }
    Task<ToolResult> ExecuteAsync(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken);
}
