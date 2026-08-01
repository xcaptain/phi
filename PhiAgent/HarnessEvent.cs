namespace PhiAgent;

/// <summary>
/// Domain-level events emitted by the <see cref="Harness"/>. The provider layer
/// emits its own wire-level events (<see cref="ProviderEvent"/>); the harness
/// translates them and adds agent semantics — tool execution lifecycle, turn
/// boundaries, and final-message delivery.
/// </summary>
public abstract record HarnessEvent;

/// <summary>A new turn has started; the harness is about to call the provider.</summary>
public sealed record TurnStartEvent(int Turn) : HarnessEvent;

/// <summary>A streamed text fragment from the assistant.</summary>
public sealed record AssistantTextDeltaEvent(string Delta) : HarnessEvent;

/// <summary>A reasoning block has begun. UI should render a "Thinking…"
/// placeholder; the block's signature (if any) only lands on the end event.</summary>
public sealed record AssistantThinkingStartEvent : HarnessEvent;

/// <summary>A streamed reasoning fragment from the assistant. Lets the UI
/// surface the model's thinking process as it generates.</summary>
public sealed record AssistantThinkingDeltaEvent(string Delta) : HarnessEvent;

/// <summary>A reasoning block has finished. Carries the consolidated
/// <see cref="ThinkingBlock"/> (including signature) so callers that didn't
/// track deltas can still pick it up.</summary>
public sealed record AssistantThinkingEndEvent(ThinkingBlock Block) : HarnessEvent;

/// <summary>The model requested a tool call (assembled from streamed deltas).</summary>
public sealed record AssistantToolCallEvent(ToolCall ToolCall) : HarnessEvent;

/// <summary>The harness is about to execute a tool call.</summary>
public sealed record ToolExecutionStartEvent(string ToolCallId, string ToolName) : HarnessEvent;

/// <summary>The harness finished executing a tool call.</summary>
public sealed record ToolExecutionEndEvent(ToolCall ToolCall, ToolResult Result) : HarnessEvent;

/// <summary>A turn has ended; the model produced a final message with no more tool calls.</summary>
public sealed record TurnEndEvent(AssistantMessage FinalMessage) : HarnessEvent;

/// <summary>The harness or agent loop encountered an exception; surfaces unrecoverable failures.</summary>
public sealed record HarnessErrorEvent(string Message) : HarnessEvent;
