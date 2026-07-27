using System.Text.Json.Nodes;

namespace PhiAgent;

/// <summary>
/// Provider-neutral streaming events. Each <c>PhiProvider</c> implementation
/// emits these; the agent loop turns them into a final <c>AssistantMessage</c>.
/// </summary>
public abstract record ProviderEvent
{
    public abstract string Kind { get; }
}

/// <summary>A streamed text fragment.</summary>
public sealed record ProviderTextDeltaEvent(string Delta) : ProviderEvent
{
    public override string Kind => "textDelta";
}

/// <summary>A complete tool call requested by the model.</summary>
public sealed record ProviderToolCallEvent(ToolCall ToolCall) : ProviderEvent
{
    public override string Kind => "toolCall";
}

/// <summary>The provider finished one assistant response; the message is fully assembled.</summary>
public sealed record ProviderResponseEndEvent(AssistantMessage Message, string? FinishReason = null) : ProviderEvent
{
    public override string Kind => "responseEnd";
}

/// <summary>A provider-level error; surfaces to the agent layer for retry or display.</summary>
public sealed record ProviderErrorEvent(string Message, JsonNode? Data = null) : ProviderEvent
{
    public override string Kind => "error";
}