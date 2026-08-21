using Phi.Agent;

namespace Phi;

/// <summary>
/// Pure aggregation: walks a message list once and produces the cumulative
/// <see cref="SessionStats"/>. No side effects, no caching — the caller
/// decides when to invoke it (on resume, on each turn end).
/// <para>
/// Input tokens follow tau's convention: <c>input + cache_read + cache_write</c>.
/// </para>
/// </summary>
public static class SessionStatsCalculator
{
    public static SessionStats Calculate(IReadOnlyList<IAgentMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var turnCount = 0;
        var toolCallCount = 0;
        var inputTokens = 0;
        var outputTokens = 0;
        var totalTokens = 0;

        foreach (var msg in messages)
        {
            switch (msg)
            {
                case UserMessage u when !u.Text.StartsWith(
                    ContextWindow.CompactionSummaryPrefix, StringComparison.Ordinal):
                    // Compaction summaries ride along as UserMessages with
                    // a marker prefix; they are infrastructure, not turns.
                    turnCount++;
                    break;
                case AssistantMessage a:
                    toolCallCount += a.ToolCalls.Count;
                    inputTokens += a.Usage.Input + a.Usage.CacheRead + a.Usage.CacheWrite;
                    outputTokens += a.Usage.Output;
                    totalTokens += a.Usage.TotalTokens;
                    break;
            }
        }

        return new SessionStats(
            TurnCount: turnCount,
            ToolCallCount: toolCallCount,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            TotalTokens: totalTokens,
            EstimatedCost: null);
    }

    /// <summary>
    /// Returns <paramref name="stats"/> with <paramref name="extra"/> added
    /// to every token field. Used to fold in the usage of LLM calls that
    /// don't appear in the message list (summarization during compaction),
    /// so the session's reported totals match what's actually been billed.
    /// </summary>
    public static SessionStats WithAddedUsage(SessionStats stats, Usage? extra)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (extra is null) return stats;
        return stats with
        {
            InputTokens = stats.InputTokens + extra.Input + extra.CacheRead + extra.CacheWrite,
            OutputTokens = stats.OutputTokens + extra.Output,
            TotalTokens = stats.TotalTokens + extra.TotalTokens,
        };
    }
}
