using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Deterministic context-size estimation and threshold policy. Mirrors
/// tau's <c>tau_coding.context_window</c>: char-based heuristic, per-message
/// and per-tool overhead constants, no LLM call. The harness's actual
/// provider still uses real billing tokens; these numbers are only for
/// compaction decisions and status-bar display.
/// </summary>
public static class ContextWindow
{
    public const int CharsPerToken = 4;
    public const int MessageOverheadTokens = 4;
    public const int ToolOverheadTokens = 16;
    public const int DefaultContextWindowTokens = 128_000;
    public const int DefaultCompactionReserveTokens = 16_384;
    public const int DefaultCompactionKeepRecentTokens = 20_000;

    /// <summary>Summary text prefix used to detect a previously-compacted
    /// session on resume; mirrors tau's <c>COMPACTION_SUMMARY_PREFIX</c>.</summary>
    public const string CompactionSummaryPrefix = "Previous conversation summary:\n";

    public static int EstimateTextTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Math.Max(1, (text.Length + CharsPerToken - 1) / CharsPerToken);
    }

    public static int EstimateMessageTokens(IAgentMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tokens = MessageOverheadTokens + EstimateTextTokens(ExtractText(message));

        switch (message)
        {
            case AssistantMessage a:
                foreach (var block in a.Content)
                {
                    switch (block)
                    {
                        case ThinkingBlock tb:
                            tokens += EstimateTextTokens(tb.Thinking);
                            break;
                        case ToolCall tc:
                            tokens += EstimateTextTokens(tc.Name) +
                                      EstimateTextTokens(tc.Arguments.ToJsonString());
                            break;
                    }
                }
                break;
            case ToolResultMessage t:
                tokens += EstimateTextTokens(t.ToolName);
                break;
        }

        return tokens;
    }

    public static int EstimateToolTokens(IHarnessTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return ToolOverheadTokens
            + EstimateTextTokens(tool.Tool.Name)
            + EstimateTextTokens(tool.Tool.Description)
            + EstimateTextTokens(tool.Tool.Parameters.ToJsonString());
    }

    public static int EstimateContextUsage(
        string system,
        IReadOnlyList<IAgentMessage> messages,
        IReadOnlyList<IHarnessTool> tools)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);

        var tokens = EstimateTextTokens(system);
        foreach (var m in messages) tokens += EstimateMessageTokens(m);
        foreach (var t in tools) tokens += EstimateToolTokens(t);
        return tokens;
    }

    /// <summary>
    /// Returns <c>context_window - reserve</c> as the auto-compact
    /// threshold, or <c>null</c> for an unknown window size. Reserves room
    /// for the next response (output tokens) so a long turn can finish.
    /// </summary>
    public static int? AutoCompactionThresholdForContextWindow(int contextWindowTokens)
    {
        if (contextWindowTokens <= 0) return null;
        return Math.Max(1, contextWindowTokens - DefaultCompactionReserveTokens);
    }

    private static string ExtractText(IAgentMessage message) => message switch
    {
        UserMessage u => u.Text,
        AssistantMessage a => a.Text,
        ToolResultMessage t => t.Text,
        BashExecutionMessage b => b.Output,
        CustomMessage c => c.Text,
        BranchSummaryMessage bs => bs.Summary,
        _ => "",
    };
}