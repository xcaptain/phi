using Phi.Agent;

namespace Phi;

/// <summary>
/// Bridge between the runtime <see cref="IAgentMessage"/> hierarchy and the
/// persisted <see cref="SessionEntry"/> hierarchy. We persist a strict
/// subset (User, Assistant, ToolResult); diagnostic / custom messages are
/// intentionally not round-tripped because they don't belong in the
/// conversation history the model sees next turn.
/// </summary>
public static class SessionEntryConverter
{
    public static SessionEntry FromAgentMessage(IAgentMessage msg) => msg switch
    {
        UserMessage u => new UserSessionEntry(u.Timestamp, u.Text),
        AssistantMessage a => new AssistantSessionEntry(
            a.Timestamp, a.Content, a.StopReason, a.Usage),
        ToolResultMessage t => new ToolResultSessionEntry(
            t.Timestamp, t.ToolCallId, t.ToolName, t.Content, t.IsError, t.Details),
        _ => throw new NotSupportedException(
            $"Session persistence does not support message type {msg.GetType().Name}"),
    };

    public static IAgentMessage ToAgentMessage(SessionEntry entry) => entry switch
    {
        UserSessionEntry u => new UserMessage
        {
            Content = u.Content,
            Timestamp = u.Timestamp,
        },
        AssistantSessionEntry a => new AssistantMessage
        {
            Content = a.Content,
            StopReason = a.StopReason,
            Timestamp = a.Timestamp,
            Usage = a.Usage,
        },
        ToolResultSessionEntry t => new ToolResultMessage
        {
            ToolCallId = t.ToolCallId,
            ToolName = t.ToolName,
            Content = t.Content,
            IsError = t.IsError,
            Timestamp = t.Timestamp,
            // Tool-specific Details (BashDetails, EditDetails, etc.) are
            // restored so resume can re-render rich tool cards. Legacy
            // transcripts (written before Details was persisted) leave this
            // null and the renderer falls back to the textual-only view.
            Details = t.Details,
        },
        CompactionSessionEntry c => new UserMessage
        {
            // The compaction entry is materialized as a user-role message
            // carrying the standard prefix, mirroring CompactionStorage's
            // live-state shape. This keeps the runtime / provider layer
            // free of any compaction-specific IAgentMessage subtype.
            Content = ContextWindow.CompactionSummaryPrefix + c.Summary,
            Timestamp = c.Timestamp,
        },
        _ => throw new NotSupportedException(
            $"Cannot restore message from session entry {entry.GetType().Name}"),
    };
}
