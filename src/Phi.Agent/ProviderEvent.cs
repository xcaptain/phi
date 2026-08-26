using System.Text.Json.Nodes;

namespace Phi.Agent;

/// <summary>
/// Provider-neutral streaming events. Each <c>Phi.Provider</c> implementation
/// emits these; the agent loop turns them into a final <c>AssistantMessage</c>.
/// <para>
/// Shape mirrors tau/pi's <c>AssistantMessageEvent</c> discriminated union in
/// <c>tau_agent/provider_events.py</c>, minus the per-event <c>partial</c>
/// snapshot (a C# record copy of the growing <c>Content</c> list would be
/// O(n²); the agent loop maintains partial state itself and forwards a
/// reference via <see cref="HarnessEvent.MessageUpdateEvent"/>).
/// <see cref="Kind"/> stays PascalCase to match C# naming rather than Pi's
/// lowercase snake_case wire literals.
/// </para>
/// <para>
/// Provider implementations emit, in order:
/// <list type="number">
/// <item><see cref="AssistantStartEvent"/> (optional — the loop already
/// synthesized <c>MessageStartEvent</c>; this is the Pi-compatible begin
/// marker)</item>
/// <item>zero or more <see cref="TextDeltaEvent"/> /
/// <see cref="ThinkingDeltaEvent"/> / <see cref="ToolCallEvent"/></item>
/// <item>zero or more <see cref="ThinkingEndEvent"/> (carries the
/// consolidated thinking block with any signature)</item>
/// <item>exactly one <see cref="AssistantDoneEvent"/> or
/// <see cref="AssistantErrorEvent"/> as terminal</item>
/// </list>
/// </para>
/// </summary>
public abstract record ProviderEvent
{
    public abstract string Kind { get; }
}

/// <summary>
/// Pi-compatible begin marker: the provider is about to emit content for a
/// new assistant message. The loop already emits <c>MessageStartEvent</c>
/// before invoking the provider stream, so this event is a no-op at the
/// loop layer; it's part of the protocol so projectors / extensions can
/// observe an explicit begin signal from the provider.
/// </summary>
public sealed record AssistantStartEvent : ProviderEvent
{
    public override string Kind => "Start";
}

/// <summary>A streamed text fragment.</summary>
public sealed record TextDeltaEvent(string Delta) : ProviderEvent
{
    public override string Kind => "TextDelta";
}

/// <summary>
/// A streamed reasoning fragment. Emitted by providers that expose extended
/// thinking (Anthropic, OpenAI o-series, DeepSeek-R1, etc.). The thinking
/// block opens lazily on the first delta (mirrors tau's
/// <c>canonicalize_provider_stream</c>) — there is no separate
/// <c>ThinkingStartEvent</c>.
/// </summary>
public sealed record ThinkingDeltaEvent(string Delta) : ProviderEvent
{
    public override string Kind => "ThinkingDelta";
}

/// <summary>
/// The reasoning block has closed. <see cref="ThinkingBlock.ThinkingSignature"/>
/// carries the consolidated signature payload (for Anthropic, the adapter
/// accumulates <c>signature_delta</c> fragments internally and surfaces the
/// result here; providers that don't separate signatures from content can
/// leave it null). The canonicalizer stamps it onto the trailing
/// <see cref="ThinkingBlock"/> via <see cref="AssistantMessageBuilder.Apply"/>.
/// </summary>
public sealed record ThinkingEndEvent(ThinkingBlock Block) : ProviderEvent
{
    public override string Kind => "ThinkingEnd";
}

/// <summary>A complete tool call requested by the model.</summary>
public sealed record ToolCallEvent(ToolCall ToolCall) : ProviderEvent
{
    public override string Kind => "ToolCall";
}

/// <summary>
/// Terminal success: the provider finished one assistant response and the
/// message is fully assembled (StopReason, Usage, Model, etc.). Content is
/// intentionally empty here — the loop keeps the streamed-order partial as
/// authoritative and adopts only StopReason / Usage / Model via
/// <see cref="AssistantMessageBuilder.AdoptFinal"/>.
/// </summary>
public sealed record AssistantDoneEvent(
    AssistantMessage Message,
    string? FinishReason = null) : ProviderEvent
{
    public override string Kind => "Done";
}

/// <summary>
/// A provider-level error that surfaces to the agent layer for retry or
/// display. Mirrors tau's <c>AssistantErrorEvent</c>.
/// </summary>
public sealed record AssistantErrorEvent(string Message, JsonNode? Data = null) : ProviderEvent
{
    public override string Kind => "Error";

    /// <summary>
    /// HTTP status code when the error came from a non-success response;
    /// null for stream-level or network failures. Lets the retry driver
    /// classify transient statuses (429/5xx) without parsing
    /// <see cref="Message"/>.
    /// </summary>
    public int? HttpStatus { get; init; }
}
