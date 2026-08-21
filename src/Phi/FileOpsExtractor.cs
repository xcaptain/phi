using Phi.Agent;

namespace Phi;

/// <summary>
/// Walks a message list once and pulls file paths out of
/// <see cref="AssistantMessage.ToolCalls"/>. Used by the compaction pipeline
/// to accumulate which files the LLM has read or modified.
/// </summary>
public static class FileOpsExtractor
{
    private const string ArgPath = "path";

    /// <summary>
    /// Returns the file operations implied by the tool calls in
    /// <paramref name="messages"/>. Tool calls whose name or argument shape
    /// we don't recognize contribute nothing.
    /// </summary>
    public static CompactionDetails Extract(IReadOnlyList<IAgentMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var read = new List<string>();
        var modified = new List<string>();
        var seenRead = new HashSet<string>(StringComparer.Ordinal);
        var seenModified = new HashSet<string>(StringComparer.Ordinal);

        foreach (var msg in messages)
        {
            if (msg is not AssistantMessage a) continue;
            foreach (var tc in a.ToolCalls)
            {
                var path = TryGetPathArg(tc.Arguments);
                if (path is null) continue;

                switch (tc.Name)
                {
                    case "read":
                        if (seenRead.Add(path)) read.Add(path);
                        break;
                    case "write":
                    case "edit":
                        if (seenModified.Add(path)) modified.Add(path);
                        break;
                }
            }
        }

        return new CompactionDetails(read, modified);
    }

    private static string? TryGetPathArg(System.Text.Json.Nodes.JsonObject arguments)
    {
        if (arguments.TryGetPropertyValue(ArgPath, out var node) &&
            node is System.Text.Json.Nodes.JsonValue v &&
            v.TryGetValue<string>(out var s) &&
            !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }
        return null;
    }
}
