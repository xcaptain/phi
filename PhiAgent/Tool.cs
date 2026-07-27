using System.Text.Json.Nodes;

namespace PhiAgent;

/// <summary>Tool definition used by the agent loop. Slim form: just what's needed
/// to advertise the tool to the LLM and dispatch tool calls back to an executor.</summary>
public sealed record Tool(
    string Name,
    string Description,
    JsonObject Parameters);

/// <summary>What a tool execution returns back to the agent loop.</summary>
public sealed record ToolResult(
    IReadOnlyList<ContentBlock> Content,
    JsonNode? Details = null,
    bool IsError = false)
{
    public string Text => string.Concat(
        Content.OfType<TextBlock>().Select(b => b.Text));
}

/// <summary>Delegate signature for executing a tool call dispatched by the harness.</summary>
public delegate Task<ToolResult> ToolExecutor(
    string toolName,
    string toolCallId,
    JsonNode arguments,
    CancellationToken cancellationToken = default);