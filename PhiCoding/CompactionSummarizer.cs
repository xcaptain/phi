using System.Runtime.CompilerServices;
using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Builds the summarization prompt and asks the provider to condense a
/// range of messages. Mirrors tau's
/// <c>build_compaction_summary_prompt</c> /
/// <c>_generate_compaction_summary</c>: structured sections on first
/// compaction, update-style prompt when a previous summary is present at
/// the start of the message list.
/// </summary>
public sealed class CompactionSummarizer
{
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

    public string BuildPrompt(IReadOnlyList<IAgentMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var previousSummary = TryExtractPreviousSummary(messages);
        var newMessages = previousSummary is not null
            ? messages.Skip(1).ToList()
            : (IList<IAgentMessage>)messages;

        var conversation = SerializeMessages(newMessages);
        var prompt = $"<conversation>\n{conversation}\n</conversation>\n\n";

        var basePrompt = previousSummary is not null
            ? UpdateSummarizationPrompt
            : SummarizationPrompt;

        if (previousSummary is not null)
            prompt += $"<previous-summary>\n{previousSummary}\n</previous-summary>\n\n";

        return prompt + basePrompt;
    }

    public async Task<string> GenerateAsync(
        IPhiProvider provider,
        string model,
        IReadOnlyList<IAgentMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(messages);

        var prompt = BuildPrompt(messages);
        var request = new List<IAgentMessage> { new UserMessage { Content = prompt } };

        var collected = new System.Text.StringBuilder();
        await foreach (var ev in provider.StreamResponseAsync(
            model, SummarizationSystemPrompt, request, [], cancellationToken)
            .WithCancellation(cancellationToken))
        {
            if (ev is ProviderTextDeltaEvent t) collected.Append(t.Delta);
        }

        var summary = collected.ToString().Trim();
        if (summary.Length == 0)
            throw new InvalidOperationException(
                "Compaction summarization returned an empty summary");
        return summary;
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

    private static string SerializeMessages(IList<IAgentMessage> messages)
    {
        if (messages.Count == 0) return "(no new messages)";

        var lines = new List<string>();
        for (var i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            var attributes = $"index={i + 1} role={m.GetType().Name.Replace("Message", "")}";
            if (m is ToolResultMessage tr)
                attributes += $" name={tr.ToolName} error={tr.IsError.ToString().ToLowerInvariant()}";
            lines.Add($"<message {attributes}>");
            var text = ExtractText(m);
            if (text.Length > 0) lines.Add(text);
            if (m is AssistantMessage a && a.ToolCalls.Count > 0)
            {
                lines.Add("<tool-calls>");
                foreach (var tc in a.ToolCalls)
                    lines.Add($"- {tc.Name}: {tc.Arguments.ToJsonString()}");
                lines.Add("</tool-calls>");
            }
            lines.Add("</message>");
        }
        return string.Join("\n", lines);
    }

    private static string ExtractText(IAgentMessage m) => m switch
    {
        UserMessage u => u.Text,
        AssistantMessage a => a.Text,
        ToolResultMessage t => t.Text,
        _ => "",
    };
}