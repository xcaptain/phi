using System.Text.Json.Serialization;

namespace PhiAgent;

/// <summary>
/// One persisted row in a session JSONL file. Each entry is independently
/// type-tagged so a session can mix user prompts, assistant turns, and
/// tool results without an outer envelope. The wire shape mirrors tau's
/// <c>tau_agent.session.entries.SessionEntry</c>.
/// <para>
/// SessionEntry is a separate type from <see cref="IAgentMessage"/>: not
/// every <see cref="IAgentMessage"/> is conversation content (e.g. diagnostics,
/// custom events), and not every persisted entry needs to round-trip back
/// into the runtime message list. Conversion happens in the application
/// layer (<c>PhiCoding</c>) so the framework stays wire-only.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(UserSessionEntry), "user")]
[JsonDerivedType(typeof(AssistantSessionEntry), "assistant")]
[JsonDerivedType(typeof(ToolResultSessionEntry), "toolResult")]
public abstract record SessionEntry(long Timestamp);

public sealed record UserSessionEntry(long Timestamp, string Content)
    : SessionEntry(Timestamp);

public sealed record AssistantSessionEntry(
    long Timestamp,
    IReadOnlyList<ContentBlock> Content,
    string StopReason)
    : SessionEntry(Timestamp);

public sealed record ToolResultSessionEntry(
    long Timestamp,
    string ToolCallId,
    string ToolName,
    IReadOnlyList<ContentBlock> Content,
    bool IsError)
    : SessionEntry(Timestamp);
