namespace Phi.Extensions.Events;

// ──────── Tool execution lifecycle ────────

/// <summary>
/// Fired when the model emits a tool call. <c>Arguments</c> is the raw
/// JSON the model produced (deserialized lazily by the host per the
/// tool's schema).
/// </summary>
public sealed record ToolExecutionStartEvent(
    string ToolCallId,
    string ToolName,
    System.Text.Json.Nodes.JsonObject Arguments) : PhiEvent;

/// <summary>
/// Fired on tool progress updates (used by long-running tools that stream
/// partial results — Sprint 1+ implementation, the field is here for
/// forward compatibility).
/// </summary>
public sealed record ToolExecutionUpdateEvent(
    string ToolCallId,
    string ToolName,
    object? PartialResult) : PhiEvent;

/// <summary>
/// Fired when the tool returns. <c>IsError</c> is the tool's
/// <c>ToolResult.IsError</c>; <c>Result</c> is the full result payload.
/// </summary>
public sealed record ToolExecutionEndEvent(
    string ToolCallId,
    string ToolName,
    Phi.Agent.ToolResult Result) : PhiEvent;
