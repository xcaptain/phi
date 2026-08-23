namespace Phi.Extensions.Events;

// ──────── Tool call / result hooks ────────

/// <summary>
/// Fired before a tool call executes. Handlers can mutate
/// <c>Arguments</c> (chain — each handler sees the previous result) or
/// block the call entirely (<c>Block = true</c>). Handler exceptions are
/// treated as <c>Block = true</c> (fail-safe).
/// </summary>
public sealed record ToolCallHookEvent(
    string ToolName,
    System.Text.Json.Nodes.JsonObject Arguments) : PhiEvent;

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
/// transcript. Handlers can rewrite <c>Content</c> / <c>Details</c>
/// (e.g. scrub secrets, augment metadata). Chain-ordered.
/// </summary>
public sealed record ToolResultHookEvent(
    string ToolName,
    System.Text.Json.Nodes.JsonObject Arguments,
    Phi.Agent.ToolResult Result) : PhiEvent;

/// <summary>
/// Result of a tool-result-hook handler. <c>Content</c> replaces the
/// result's <c>Content</c> blocks; <c>Details</c> replaces the result's
/// <c>Details</c>. Both default to null (no change).
/// </summary>
public sealed record ToolResultHookResult
{
    public IReadOnlyList<Phi.Agent.ContentBlock>? Content { get; init; }
    public object? Details { get; init; }

    /// <summary>Pass-through (result kept as-is).</summary>
    public static readonly ToolResultHookResult PassThrough = new();
}
