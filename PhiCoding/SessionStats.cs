namespace PhiCoding;

/// <summary>
/// Cumulative activity and billed usage for one session branch. Derived
/// from the message list by <see cref="SessionStatsCalculator"/> and
/// surfaced to the UI through <see cref="SessionState.Stats"/>.
/// <para>
/// Mirrors tau's <c>tau_coding.session_stats.SessionStats</c>:
/// <c>turn_count</c>, <c>tool_call_count</c>, billed tokens. The optional
/// <see cref="EstimatedCost"/> field is reserved for future pricing-resolver
/// integration; for now it's always <c>null</c>.
/// </para>
/// </summary>
public sealed record SessionStats(
    int TurnCount,
    int ToolCallCount,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    double? EstimatedCost)
{
    /// <summary>Zero stats — the value for a fresh / empty session.</summary>
    public static readonly SessionStats Zero = new(0, 0, 0, 0, 0, null);
}