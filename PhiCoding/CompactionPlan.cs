using PhiAgent;

namespace PhiCoding;

/// <summary>
/// The split between messages to summarize and messages to keep, computed
/// by <see cref="CompactionPlanner"/>.
/// <para>
/// On a normal cut the planner lands at a user message and
/// <see cref="MessagesToSummarize"/> is everything before that boundary;
/// <see cref="TurnPrefixMessages"/> is empty. When no user boundary fits in
/// the recent-token budget (single huge turn), the planner falls back to a
/// "split turn": the cut lands at an assistant message mid-turn, and
/// <see cref="TurnPrefixMessages"/> carries the early portion of that turn
/// (from its user message up to the cut) so the LLM also sees the start of
/// the work that produced the kept suffix.
/// </para>
/// </summary>
public sealed record CompactionPlan(
    IReadOnlyList<IAgentMessage> MessagesToSummarize,
    IReadOnlyList<IAgentMessage> TurnPrefixMessages,
    IReadOnlyList<IAgentMessage> KeptMessages)
{
    /// <summary>True when the cut landed mid-turn and
    /// <see cref="TurnPrefixMessages"/> should be summarized alongside the
    /// history.</summary>
    public bool IsSplitTurn => TurnPrefixMessages.Count > 0;
}
