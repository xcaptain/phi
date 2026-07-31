using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Decides which messages to summarize vs. keep for a compaction. Mirrors
/// tau's <c>_recent_preserving_compaction_plan</c>: walks backward from the
/// end of the message list accumulating tokens until reaching
/// <see cref="ContextWindow.DefaultCompactionKeepRecentTokens"/>, then
/// adjusts forward to the next user-message boundary so a turn is never
/// split.
/// </summary>
public static class CompactionPlanner
{
    public static CompactionPlan? Build(
        IReadOnlyList<IAgentMessage> messages,
        int keepRecentTokens = ContextWindow.DefaultCompactionKeepRecentTokens)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count < 2) return null;

        var candidateIndex = FindRecentCutoff(messages, keepRecentTokens);
        if (candidateIndex <= 0) return null;

        var firstKeptIndex = AdjustToBoundary(messages, candidateIndex);
        if (firstKeptIndex <= 0) return null;

        var toSummarize = messages.Take(firstKeptIndex).ToList();
        var kept = messages.Skip(firstKeptIndex).ToList();
        return new CompactionPlan(toSummarize, kept);
    }

    private static int FindRecentCutoff(
        IReadOnlyList<IAgentMessage> messages, int keepRecentTokens)
    {
        if (keepRecentTokens <= 0) return messages.Count;

        var accumulated = 0;
        var candidate = -1;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            accumulated += ContextWindow.EstimateMessageTokens(messages[i]);
            if (accumulated >= keepRecentTokens)
            {
                candidate = i;
                break;
            }
        }
        return candidate < 0 ? 0 : candidate;
    }

    private static int AdjustToBoundary(
        IReadOnlyList<IAgentMessage> messages, int candidate)
    {
        if (candidate >= messages.Count) return messages.Count;

        if (IsUserMessage(messages[candidate]))
        {
            return candidate > 0 ? candidate : NextUserIndex(messages, 1) ?? 0;
        }

        var nextUser = NextUserIndex(messages, candidate + 1);
        if (nextUser is not null) return nextUser.Value;

        for (var i = candidate; i < messages.Count; i++)
        {
            if (!IsToolResultMessage(messages[i])) return i;
        }
        return messages.Count;
    }

    private static bool IsUserMessage(IAgentMessage m) => m is UserMessage;
    private static bool IsToolResultMessage(IAgentMessage m) => m is ToolResultMessage;

    private static int? NextUserIndex(
        IReadOnlyList<IAgentMessage> messages, int start)
    {
        for (var i = start; i < messages.Count; i++)
        {
            if (messages[i] is UserMessage) return i;
        }
        return null;
    }
}