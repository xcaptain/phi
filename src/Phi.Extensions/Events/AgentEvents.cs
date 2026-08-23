namespace Phi.Extensions.Events;

// ──────── Agent lifecycle ────────

/// <summary>
/// Fired at the start of <c>Session.SubmitPrompt</c> (before the first
/// <c>TurnStartEvent</c>). The session is marked <c>IsRunning = true</c>
/// by the time handlers observe this event.
/// </summary>
public sealed record AgentStartEvent() : PhiEvent;

/// <summary>
/// Fired at the end of the agent run (after the final <c>TurnEndEvent</c>
/// and the per-message flush). <c>WillRetry</c> is true only if a future
/// host feature schedules a retry (Phi v1 doesn't, but the field is here
/// for forward compatibility).
/// </summary>
public sealed record AgentEndEvent(
    IReadOnlyList<Phi.Agent.IAgentMessage> Messages,
    bool WillRetry) : PhiEvent;

/// <summary>
/// Fired when the agent has no pending work (no in-flight turn, no queued
/// steering / follow-up, no compaction in progress, no auto-retry pending).
/// Useful for "the model is done thinking, you can stop polling" signals.
/// </summary>
public sealed record AgentSettledEvent() : PhiEvent;

// ──────── Turn ────────

/// <summary>
/// Fired at the start of each iteration of the agent loop (after the model
/// round-trip, before tool execution). <c>TurnIndex</c> is 1-based and
/// matches the host's existing <c>TurnStartEvent</c>.
/// </summary>
public sealed record TurnStartEvent(int TurnIndex, long TimestampMs) : PhiEvent;

/// <summary>
/// Fired at the end of each iteration (whether the turn ended naturally,
/// via tool calls, or via error/abort). <c>ToolResults</c> is empty when
/// the turn ended with no tool calls.
/// </summary>
public sealed record TurnEndEvent(
    int TurnIndex,
    Phi.Agent.AssistantMessage FinalMessage,
    IReadOnlyList<Phi.Agent.ToolResultMessage> ToolResults) : PhiEvent;

// ──────── Message streaming ────────

/// <summary>
/// Fired when the model begins emitting an assistant message (text or
/// tool calls). <c>Message</c> may still be incomplete at this point —
/// follow <c>MessageUpdateEvent</c>s for deltas.
/// </summary>
public sealed record MessageStartEvent(Phi.Agent.AssistantMessage Message) : PhiEvent;

/// <summary>
/// Fired on each streaming delta. <c>AssistantMessageEvent</c> is the
/// raw provider-level event (text delta, thinking delta, tool call, etc.).
/// </summary>
public sealed record MessageUpdateEvent(
    Phi.Agent.AssistantMessage Message,
    Phi.Agent.ProviderEvent AssistantMessageEvent) : PhiEvent;

/// <summary>
/// Fired when the model finishes emitting an assistant message.
/// <c>Message</c> is final (including usage stats if reported).
/// </summary>
public sealed record MessageEndEvent(Phi.Agent.AssistantMessage Message) : PhiEvent;
