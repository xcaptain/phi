namespace Phi.Extensions.Events;

// ──────── Tool call / result hooks ────────

/// <summary>
/// Fired before a tool call executes. Handlers set <see cref="Result"/> to
/// block or transform. The dispatcher chains handlers: each sees the
/// (possibly transformed) <see cref="Arguments"/> from the previous
/// handler. Handler exceptions are treated as <c>Block = true</c>
/// (fail-safe).
/// <para>
/// <c>Result</c> is the settable slot a handler fills; null = pass-through.
/// </para>
/// </summary>
public sealed record ToolCallHookEvent(
    string ToolName,
    System.Text.Json.Nodes.JsonObject Arguments) : PhiEvent
{
    /// <summary>
    /// Set by a handler to block (<see cref="ToolCallHookResult.Block"/>)
    /// or transform (<see cref="ToolCallHookResult.Arguments"/>). Null =
    /// pass-through.
    /// </summary>
    public ToolCallHookResult? Result { get; set; }
}

/// <summary>
/// Result of a tool-call-hook handler. <c>Block = true</c> stops the
/// call; <c>Reason</c> appears in the assistant message that gets fed
/// back to the model (so it knows why the call was blocked).
/// </summary>
public sealed record ToolCallHookResult
{
    public bool Block { get; init; }
    public string? Reason { get; init; }
    public System.Text.Json.Nodes.JsonObject? Arguments { get; init; }

    /// <summary>Pass-through (let the call execute as-is).</summary>
    public static readonly ToolCallHookResult PassThrough = new() { Block = false };
}

/// <summary>
/// Fired after a tool executes, before the result is appended to the
/// transcript. Handlers set <see cref="Result"/> to rewrite
/// <c>Content</c> / <c>Details</c> (e.g. scrub secrets, augment
/// metadata). Chain-ordered — each handler sees the previous result.
/// </summary>
public sealed record ToolResultHookEvent(
    string ToolName,
    System.Text.Json.Nodes.JsonObject Arguments,
    Phi.Agent.ToolResult Result) : PhiEvent
{
    /// <summary>
    /// Set by a handler to rewrite the result
    /// (<see cref="ToolResultHookResult.Content"/> /
    /// <see cref="ToolResultHookResult.Details"/>). Null = pass-through.
    /// </summary>
    public ToolResultHookResult? Rewrite { get; set; }
}

/// <summary>
/// Result of a tool-result-hook handler. <c>Content</c> replaces the
/// result's <c>Content</c> blocks; <c>Details</c> replaces the result's
/// <c>Details</c>. Both default to null (no change).
/// </summary>
public sealed record ToolResultHookResult
{
    public IReadOnlyList<Phi.Agent.ContentBlock>? Content { get; init; }
    public System.Text.Json.Nodes.JsonNode? Details { get; init; }

    /// <summary>Pass-through (result kept as-is).</summary>
    public static readonly ToolResultHookResult PassThrough = new();
}
