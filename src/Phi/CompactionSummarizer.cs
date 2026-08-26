using Phi.Agent;

namespace Phi;

/// <summary>
/// Builds the summarization prompt and asks the provider to condense a
/// range of messages. Mirrors pi's <c>build_compaction_summary_prompt</c> /
/// <c>_generate_compaction_summary</c>: structured sections on first
/// compaction, update-style prompt when a previous summary is present at
/// the start of the message list. Also mirrors pi's message serialization
/// (<c>[User]: …</c>, <c>[Assistant]: …</c>, <c>[Tool result] (name): …</c>)
/// and tool-result truncation so the summarization prompt stays within a
/// reasonable budget regardless of how large a single read/bash output was.
/// </summary>
public sealed class CompactionSummarizer
{
    /// <summary>Tool-result bodies longer than this are truncated in the
    /// summary prompt; the source-of-truth message stays full size on disk
    /// and in the harness.</summary>
    public const int ToolResultTruncateChars = 2_000;

    public const string SummarizationSystemPrompt =
        "You are a context summarization assistant. Your task is to read a conversation " +
        "between a user and an AI coding assistant, then produce a structured summary " +
        "following the exact format specified.\n\n" +
        "Do NOT continue the conversation. Do NOT respond to any questions in the " +
        "conversation. ONLY output the structured summary.";

    public const string SummarizationPrompt =
        "The messages above are a conversation to summarize. Create a structured context " +
        "checkpoint summary that another LLM will use to continue the work.\n\n" +
        "Use this EXACT format:\n\n" +
        "## Goal\n" +
        "[What is the user trying to accomplish? Can be multiple items if the session " +
        "covers different tasks.]\n\n" +
        "## Constraints & Preferences\n" +
        "- [Any constraints, preferences, or requirements mentioned by user]\n" +
        "- [Or \"(none)\" if none were mentioned]\n\n" +
        "## Progress\n" +
        "### Done\n" +
        "- [x] [Completed tasks/changes]\n\n" +
        "### In Progress\n" +
        "- [ ] [Current work]\n\n" +
        "### Blocked\n" +
        "- [Issues preventing progress, if any]\n\n" +
        "## Key Decisions\n" +
        "- **[Decision]**: [Brief rationale]\n\n" +
        "## Next Steps\n" +
        "1. [Ordered list of what should happen next]\n\n" +
        "## Critical Context\n" +
        "- [Any data, examples, or references needed to continue]\n" +
        "- [Or \"(none)\" if not applicable]\n\n" +
        "Keep each section concise. Preserve exact file paths, function names, and error " +
        "messages.";

    public const string UpdateSummarizationPrompt =
        "The messages above are NEW conversation messages to incorporate into the existing " +
        "summary provided in <previous-summary> tags.\n\n" +
        "Update the existing structured summary with new information. RULES:\n" +
        "- PRESERVE all existing information from the previous summary\n" +
        "- ADD new progress, decisions, and context from the new messages\n" +
        "- UPDATE the Progress section: move items from \"In Progress\" to \"Done\" when " +
        "completed\n" +
        "- UPDATE \"Next Steps\" based on what was accomplished\n" +
        "- PRESERVE exact file paths, function names, and error messages\n" +
        "- If something is no longer relevant, you may remove it\n\n" +
        "Use this EXACT format:\n\n" +
        "## Goal\n" +
        "[Preserve existing goals, add new ones if the task expanded]\n\n" +
        "## Constraints & Preferences\n" +
        "- [Preserve existing, add new ones discovered]\n\n" +
        "## Progress\n" +
        "### Done\n" +
        "- [x] [Include previously done items AND newly completed items]\n\n" +
        "### In Progress\n" +
        "- [ ] [Current work - update based on progress]\n\n" +
        "### Blocked\n" +
        "- [Current blockers - remove if resolved]\n\n" +
        "## Key Decisions\n" +
        "- **[Decision]**: [Brief rationale] (preserve all previous, add new)\n\n" +
        "## Next Steps\n" +
        "1. [Update based on current state]\n\n" +
        "## Critical Context\n" +
        "- [Preserve important context, add new if applicable]\n\n" +
        "Keep each section concise. Preserve exact file paths, function names, and error " +
        "messages.";

    /// <summary>
    /// Builds the prompt for summarizing <paramref name="messages"/> (the
    /// history preceding the cut). When <paramref name="turnPrefixMessages"/>
    /// is non-empty the cut landed mid-turn and those messages are appended
    /// after a separator so the LLM also sees the early part of the current
    /// turn. <paramref name="previousDetails"/> carries the cumulative
    /// read/modified files from the previous compaction, if any.
    /// </summary>
    public static string BuildPrompt(
        IReadOnlyList<IAgentMessage> messages,
        IReadOnlyList<IAgentMessage>? turnPrefixMessages = null,
        CompactionDetails? previousDetails = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var previousSummary = TryExtractPreviousSummary(messages);
        // The compaction summary rides along as messages[0] (UserMessage with
        // CompactionSummaryPrefix); skip it from the serialized history so
        // the LLM sees the raw messages, not the previous summary embedded
        // twice (it lives in <previous-summary> below).
        var historyMessages = previousSummary is not null
            ? messages.Skip(1).ToList()
            : messages.ToList();

        var prompt = "<conversation>\n" +
                     SerializeMessages(historyMessages) +
                     "\n</conversation>\n";

        if (turnPrefixMessages is { Count: > 0 } prefix)
        {
            prompt += "\n[Current turn — early portion]\n" +
                      SerializeMessages(prefix.ToList()) +
                      "\n[/Current turn — early portion]\n";
        }

        if (previousSummary is not null)
        {
            prompt += $"\n<previous-summary>\n{previousSummary}\n</previous-summary>\n";
        }

        if (previousDetails is not null)
        {
            if (previousDetails.ReadFiles.Count > 0)
            {
                prompt += "\n<read-files>\n" +
                          string.Join("\n", previousDetails.ReadFiles) +
                          "\n</read-files>\n";
            }
            if (previousDetails.ModifiedFiles.Count > 0)
            {
                prompt += "\n<modified-files>\n" +
                          string.Join("\n", previousDetails.ModifiedFiles) +
                          "\n</modified-files>\n";
            }
        }

        var basePrompt = previousSummary is not null
            ? UpdateSummarizationPrompt
            : SummarizationPrompt;

        return prompt + "\n" + basePrompt;
    }

    /// <summary>
    /// Result of a summarization call: the generated summary text plus the
    /// token usage of the LLM call itself, so the session's billed totals
    /// can include summarization work.
    /// </summary>
    public sealed record SummaryResult(string Text, Usage Usage);

    public static async Task<SummaryResult> GenerateAsync(
        IPhiProvider provider,
        string model,
        IReadOnlyList<IAgentMessage> messages,
        IReadOnlyList<IAgentMessage>? turnPrefixMessages = null,
        CompactionDetails? previousDetails = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(messages);

        var prompt = BuildPrompt(messages, turnPrefixMessages, previousDetails);
        var request = new List<IAgentMessage> { new UserMessage { Content = prompt } };

        var partial = new AssistantMessage
        {
            StopReason = StopReasons.Stop,
        };
        var usage = new Usage();
        await foreach (var ev in provider.StreamResponseAsync(
            model, SummarizationSystemPrompt, request, [], cancellationToken)
            .WithCancellation(cancellationToken))
        {
            switch (ev)
            {
                case AssistantDoneEvent end:
                    // Adopt the terminal usage — AdoptFinal skips Content
                    // (the streamed order is authoritative) but the
                    // terminal's usage/stats are the authoritative source.
                    if (end.Message.Usage is { } u) usage = u;
                    break;
                default:
                    // Accumulate the running partial via the same
                    // canonicalizer the agent loop uses.
                    partial = AssistantMessageBuilder.Apply(partial, ev);
                    break;
            }
        }

        var summary = partial.Text.Trim();
        if (summary.Length == 0)
            throw new InvalidOperationException(
                "Compaction summarization returned an empty summary");
        return new SummaryResult(summary, usage);
    }

    private static string? TryExtractPreviousSummary(IReadOnlyList<IAgentMessage> messages)
    {
        if (messages.Count == 0) return null;
        if (messages[0] is not UserMessage u) return null;
        var text = u.Text;
        if (!text.StartsWith(ContextWindow.CompactionSummaryPrefix,
                StringComparison.Ordinal))
            return null;
        return text[ContextWindow.CompactionSummaryPrefix.Length..];
    }

    /// <summary>
    /// Serializes messages in pi's
    /// <c>[User]: …</c> / <c>[Assistant]: …</c> / <c>[Assistant tool calls]: …</c> /
    /// <c>[Tool result] (name): …</c> format. Tool-result bodies longer than
    /// <see cref="ToolResultTruncateChars"/> are truncated so a single
    /// oversized read/bash output doesn't blow the summary prompt budget.
    /// </summary>
    private static string SerializeMessages(List<IAgentMessage> messages)
    {
        if (messages.Count == 0) return "(no messages)";

        var lines = new List<string>();
        foreach (var m in messages)
        {
            switch (m)
            {
                case UserMessage u:
                    lines.Add("[User]: " + u.Text);
                    break;

                case AssistantMessage a:
                    var thinking = a.ThinkingText;
                    if (!string.IsNullOrEmpty(thinking))
                        lines.Add("[Assistant thinking]: " + thinking);
                    var text = a.Text;
                    if (!string.IsNullOrEmpty(text))
                        lines.Add("[Assistant]: " + text);
                    if (a.ToolCalls.Count > 0)
                    {
                        var calls = string.Join("; ",
                            a.ToolCalls.Select(FormatToolCall));
                        lines.Add("[Assistant tool calls]: " + calls);
                    }
                    break;

                case ToolResultMessage tr:
                    lines.Add($"[Tool result] ({tr.ToolName}): " +
                              Truncate(tr.Text, ToolResultTruncateChars));
                    break;

                case BashExecutionMessage bash:
                    lines.Add("[BashExecution]: " +
                              Truncate(bash.Output, ToolResultTruncateChars));
                    break;

                case CustomMessage c:
                    lines.Add("[Custom]: " + c.Text);
                    break;

                case BranchSummaryMessage bs:
                    lines.Add("[BranchSummary]: " + bs.Summary);
                    break;
            }
        }
        return string.Join("\n", lines);
    }

    private static string FormatToolCall(ToolCall tc)
    {
        var args = tc.Arguments.ToJsonString();
        return $"{tc.Name}({args})";
    }

    private static string Truncate(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
        var omitted = text.Length - maxChars;
        return text[..maxChars] + $"\n[...truncated {omitted} chars]";
    }
}
