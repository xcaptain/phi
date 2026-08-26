using System.Text.Json.Nodes;

namespace Phi.Agent;

/// <summary>
/// Domain-level events emitted by the <see cref="Harness"/>. The provider layer
/// emits its own wire-level events (<see cref="ProviderEvent"/>); the harness
/// translates them into the agent-level envelope used by UI projectors,
/// extension hooks, and the status router.
/// <para>
/// The shape mirrors <c>tau_agent.events</c> (Pi-compatible):
/// <list type="bullet">
/// <item><see cref="AgentStartEvent"/> / <see cref="AgentEndEvent"/> — envelope around an agent invocation (one <c>SubmitPrompt</c>).</item>
/// <item><see cref="TurnStartEvent"/> / <see cref="TurnEndEvent"/> — envelope around one model round-trip plus its tool execution.</item>
/// <item><see cref="MessageStartEvent"/> / <see cref="MessageUpdateEvent"/> / <see cref="MessageEndEvent"/> — per-message envelope. <see cref="MessageUpdateEvent"/> carries the running <see cref="AssistantMessage"/> partial plus the source <see cref="ProviderEvent"/> so consumers can dispatch on the original event type while reading the canonical partial state.</item>
/// <item><see cref="ToolExecutionStartEvent"/> / <see cref="ToolExecutionEndEvent"/> — per-tool envelope, fired around each <see cref="Tool.ExecuteAsync"/> call.</item>
/// </list>
/// </summary>
public abstract record HarnessEvent;

/// <summary>An agent invocation has started (a <c>SubmitPrompt</c> reached the harness).</summary>
public sealed record AgentStartEvent : HarnessEvent;

/// <summary>
/// An agent invocation has ended. <paramref name="Messages"/> is the list of
/// messages accumulated during this invocation (everything added since
/// <see cref="AgentStartEvent"/>), excluding the historical context.
/// </summary>
public sealed record AgentEndEvent(IReadOnlyList<IAgentMessage> Messages) : HarnessEvent;

/// <summary>A new turn has started; the harness is about to call the provider.</summary>
public sealed record TurnStartEvent(int Turn) : HarnessEvent;

/// <summary>
/// A turn has ended. <paramref name="Message"/> is the assistant message that
/// terminated the turn (always present, including for tool-use turns whose
/// message contains the tool calls). <paramref name="ToolResults"/> carries
/// the <see cref="ToolResultMessage"/>s produced during this turn, or null
/// when the turn ended without tool calls.
/// </summary>
public sealed record TurnEndEvent(
    AssistantMessage Message,
    IReadOnlyList<ToolResultMessage>? ToolResults = null) : HarnessEvent;

/// <summary>
/// A message has started landing in the conversation. For streamed assistant
/// messages this fires before the first <see cref="MessageUpdateEvent"/>; for
/// non-streamed messages (user prompts, steering, follow-ups, tool results,
/// synthesized errors) this is paired with an immediate
/// <see cref="MessageEndEvent"/>.
/// </summary>
public sealed record MessageStartEvent(IAgentMessage Message) : HarnessEvent;

/// <summary>
/// A streamed message has been updated. <paramref name="Message"/> is the
/// running <see cref="AssistantMessage"/> partial at this point; the partial
/// is rebuilt from the original provider stream (see
/// <see cref="Phi.Provider.OpenAICompatibleProvider.StreamOnceAsync"/> and
/// <see cref="Phi.Provider.Anthropic"/>) and forwarded by the agent loop. The
/// provider-level event that triggered this update is forwarded as
/// <paramref name="ProviderEvent"/> so consumers can dispatch on the
/// original event type (<c>Start</c>, <c>TextDelta</c>,
/// <c>ThinkingDelta</c>, <c>ThinkingEnd</c>, <c>ToolCall</c>, <c>Done</c>,
/// <c>Error</c>) without re-parsing the partial.
/// </summary>
public sealed record MessageUpdateEvent(
    AssistantMessage Message,
    ProviderEvent ProviderEvent) : HarnessEvent;

/// <summary>A streamed message has finished landing. <paramref name="Message"/> is the final message.</summary>
public sealed record MessageEndEvent(IAgentMessage Message) : HarnessEvent;

/// <summary>
/// The harness is about to execute a tool call. <paramref name="Args"/> is
/// the raw JSON the model produced for the call, when known.
/// </summary>
public sealed record ToolExecutionStartEvent(
    string ToolCallId,
    string ToolName,
    JsonObject? Args = null) : HarnessEvent;

/// <summary>
/// The harness finished executing a tool call.
/// </summary>
public sealed record ToolExecutionEndEvent(
    string ToolCallId,
    string ToolName,
    ToolResult Result,
    bool IsError) : HarnessEvent;
