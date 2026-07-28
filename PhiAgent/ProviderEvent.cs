using System.Text.Json.Nodes;

namespace PhiAgent;

/// <summary>
/// Provider-neutral streaming events. Each <c>PhiProvider</c> implementation
/// emits these; the agent loop turns them into a final <c>AssistantMessage</c>.
/// <para>
/// <see cref="Kind"/> is a PascalCase identifier for the concrete event
/// subclass — kept in C# naming style rather than copying any upstream
/// provider's wire-format conventions.
/// </para>
/// </summary>
public abstract record ProviderEvent
{
    public abstract string Kind { get; }
}

/// <summary>A streamed text fragment.</summary>
public sealed record ProviderTextDeltaEvent(string Delta) : ProviderEvent
{
    public override string Kind => "TextDelta";
}

/// <summary>A reasoning block has begun. Lets the UI render a "Thinking…"
/// placeholder while the model reasons.</summary>
public sealed record ProviderThinkingStartEvent : ProviderEvent
{
    public override string Kind => "ThinkingStart";
}

/// <summary>A streamed reasoning fragment. Emitted by providers that expose
/// extended thinking (Anthropic, OpenAI o-series, DeepSeek-R1, etc.).</summary>
public sealed record ProviderThinkingDeltaEvent(string Delta) : ProviderEvent
{
    public override string Kind => "ThinkingDelta";
}

/// <summary>A reasoning block has finished. The consolidated <c>ThinkingBlock</c>
/// (with any signature) is included for callers that don't track deltas.</summary>
public sealed record ProviderThinkingEndEvent(ThinkingBlock Block) : ProviderEvent
{
    public override string Kind => "ThinkingEnd";
}

/// <summary>A complete tool call requested by the model.</summary>
public sealed record ProviderToolCallEvent(ToolCall ToolCall) : ProviderEvent
{
    public override string Kind => "ToolCall";
}

/// <summary>The provider finished one assistant response; the message is fully assembled.</summary>
public sealed record ProviderResponseEndEvent(AssistantMessage Message, string? FinishReason = null) : ProviderEvent
{
    public override string Kind => "ResponseEnd";
}

/// <summary>A provider-level error; surfaces to the agent layer for retry or display.</summary>
public sealed record ProviderErrorEvent(string Message, JsonNode? Data = null) : ProviderEvent
{
    public override string Kind => "Error";
}