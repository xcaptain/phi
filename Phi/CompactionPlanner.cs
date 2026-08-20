using Phi.Agent;

namespace Phi;

/// <summary>
/// Decides which messages to summarize vs. keep for a compaction. Mirrors
/// pi's <c>prepareCompaction</c>: walks backward from the end of the message
/// list accumulating tokens until reaching
/// <see cref="ContextWindow.DefaultCompactionKeepRecentTokens"/>, then lands
/// at the next user-message boundary for a normal cut, or falls back to a
/// split turn when no user boundary fits in the recent budget.
/// </summary>
public static class CompactionPlanner
{
    public static CompactionPlan? Build(
        IReadOnlyList<IAgentMessage> messages,
        int keepRecentTokens = ContextWindow.DefaultCompactionKeepRecentTokens)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count < 2) return null;

        var candidate = FindRecentCutoff(messages, keepRecentTokens);
        if (candidate <= 0 || candidate >= messages.Count) return null;

        int firstKept;
        bool isSplitTurn;

        if (messages[candidate] is UserMessage)
        {
            firstKept = candidate;
            isSplitTurn = false;
        }
        else
        {
            var nextUser = NextUserIndex(messages, candidate + 1);
            if (nextUser is not null)
            {
                // Recent window contains a user boundary → snap forward,
                // matching pi's normal-cut behavior. The current turn fits
                // in keepRecentTokens; the dropped prefix is everything
                // before it.
                firstKept = nextUser.Value;
                isSplitTurn = false;
            }
            else
            {
                // No user boundary in the recent window → split turn. Cut
                // at the first non-tool-result at or after the candidate so
                // tool results stay attached to their tool call.
                firstKept = FirstNonToolResultIndex(messages, candidate);
                isSplitTurn = true;
            }
        }

        // firstKept == messages.Count means KeptMessages would be empty,
        // which would either drop the entire transcript (normal cut) or
        // leave nothing for CompactionStorage to map kept entries to
        // (split turn). Both are destructive — refuse and let the caller
        // try again with a larger budget.
        if (firstKept <= 0 || firstKept >= messages.Count) return null;

        if (!isSplitTurn)
        {
            var toSummarize = messages.Take(firstKept).ToList();
            var kept = messages.Skip(firstKept).ToList();
            return new CompactionPlan(toSummarize, [], kept);
        }

        // Split-turn: the cut landed mid-turn. The "current turn" starts at
        // the previous user message (or index 0 if there isn't one — i.e.
        // the giant turn is the whole conversation). Everything before the
        // current turn is the history-to-summarize; the slice from the
        // turn's user up to firstKept is the turn prefix.
        var turnStart = PreviousUserIndex(messages, firstKept - 1) ?? 0;
        var historyToSummarize = messages.Take(turnStart).ToList();
        var turnPrefix = messages
            .Skip(turnStart)
            .Take(firstKept - turnStart)
            .ToList();
        var keptMessages = messages.Skip(firstKept).ToList();
        return new CompactionPlan(historyToSummarize, turnPrefix, keptMessages);
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

    private static int? NextUserIndex(
        IReadOnlyList<IAgentMessage> messages, int start)
    {
        for (var i = start; i < messages.Count; i++)
        {
            if (messages[i] is UserMessage) return i;
        }
        return null;
    }

    private static int? PreviousUserIndex(
        IReadOnlyList<IAgentMessage> messages, int start)
    {
        for (var i = start; i >= 0; i--)
        {
            if (messages[i] is UserMessage) return i;
        }
        return null;
    }

    private static int FirstNonToolResultIndex(
        IReadOnlyList<IAgentMessage> messages, int start)
    {
        for (var i = start; i < messages.Count; i++)
        {
            if (messages[i] is not ToolResultMessage) return i;
        }
        return messages.Count;
    }
}
