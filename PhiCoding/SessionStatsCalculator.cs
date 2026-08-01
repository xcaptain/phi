using PhiAgent;

namespace PhiCoding;

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
}
