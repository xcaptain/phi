using PhiAgent;

namespace PhiCoding;

/// <summary>
/// The split between messages to summarize and messages to keep, computed
/// by <see cref="CompactionPlanner"/>.
/// </summary>
public sealed record CompactionPlan(
    IReadOnlyList<IAgentMessage> MessagesToSummarize,
    IReadOnlyList<IAgentMessage> KeptMessages);
