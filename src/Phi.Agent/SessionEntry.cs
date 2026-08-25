using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Phi.Agent;

/// <summary>
/// One persisted row in a session JSONL file. Each entry is independently
/// type-tagged so a session can mix user prompts, assistant turns, and
/// tool results without an outer envelope. The wire shape mirrors tau's
/// <c>tau_agent.session.entries.SessionEntry</c>.
/// <para>
/// SessionEntry is a separate type from <see cref="IAgentMessage"/>: not
/// every <see cref="IAgentMessage"/> is conversation content (e.g. diagnostics,
/// custom events), and not every persisted entry needs to round-trip back
/// into the runtime message list. Conversion happens in the application
/// layer (<c>Phi</c>) so the framework stays wire-only.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(UserSessionEntry), "user")]
[JsonDerivedType(typeof(AssistantSessionEntry), "assistant")]
[JsonDerivedType(typeof(ToolResultSessionEntry), "toolResult")]
[JsonDerivedType(typeof(CompactionSessionEntry), "compaction")]
[JsonDerivedType(typeof(CustomSessionEntry), "custom")]
[JsonDerivedType(typeof(ExtensionSessionEntry), "extension")]
public abstract record SessionEntry(long Timestamp);

public sealed record UserSessionEntry(long Timestamp, string Content)
    : SessionEntry(Timestamp);

public sealed record AssistantSessionEntry(
    long Timestamp,
    IReadOnlyList<ContentBlock> Content,
    string StopReason,
    Usage Usage)
    : SessionEntry(Timestamp);

// `Details` carries tool-specific structured info (BashDetails, EditDetails,
// etc.) the runtime emitted alongside the textual `Content`. Persisted so a
// resume can re-render rich tool cards (side-by-side diff for `edit`,
// exit/duration for `bash`) instead of falling back to the textual-only
// fallback. Optional: legacy transcripts written before this field was added
// deserialize as null and the renderer degrades gracefully.
public sealed record ToolResultSessionEntry(
    long Timestamp,
    string ToolCallId,
    string ToolName,
    IReadOnlyList<ContentBlock> Content,
    bool IsError,
    JsonNode? Details = null)
    : SessionEntry(Timestamp);

/// <summary>
/// Cumulative file operations carried forward from a previous compaction,
/// so the next summarization prompt can list every file the LLM has read or
/// modified across the session's history. Merged (not overwritten) on each
/// compaction via <see cref="Merge"/>.
/// </summary>
public sealed record CompactionDetails(
    IReadOnlyList<string> ReadFiles,
    IReadOnlyList<string> ModifiedFiles)
{
    /// <summary>Zero details — the value before the first compaction.</summary>
    public static readonly CompactionDetails Empty = new([], []);

    /// <summary>
    /// Returns a new <see cref="CompactionDetails"/> containing all paths
    /// from <c>this</c> followed by any new paths from <paramref name="other"/>,
    /// deduplicated by ordinal comparison while preserving first-seen order.
    /// </summary>
    public CompactionDetails Merge(CompactionDetails? other)
    {
        if (other is null) return this;
        return new CompactionDetails(
            UnionPreservingOrder(ReadFiles, other.ReadFiles),
            UnionPreservingOrder(ModifiedFiles, other.ModifiedFiles));
    }

    private static IReadOnlyList<string> UnionPreservingOrder(
        IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0) return b;
        if (b.Count == 0) return a;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(a.Count + b.Count);
        foreach (var p in a)
        {
            if (seen.Add(p)) result.Add(p);
        }
        foreach (var p in b)
        {
            if (seen.Add(p)) result.Add(p);
        }
        return result;
    }
}

/// <summary>
/// Marks a point in the transcript where older messages were replaced by
/// <see cref="Summary"/>. Used by in-place compaction rewrites. Older
/// entries below this one are dropped from the JSONL file.
/// <para>
/// <see cref="Details"/> carries the cumulative read/modified files so the
/// next compaction inherits them; <see cref="Usage"/> records the token
/// usage of the summary LLM call so the session's billed totals include
/// summarization work. Both are optional and absent on entries written
/// before compaction details/usage tracking was added.
/// </para>
/// </summary>
public sealed record CompactionSessionEntry(
    long Timestamp,
    string Summary,
    int TokensBefore,
    CompactionDetails? Details = null,
    Usage? Usage = null)
    : SessionEntry(Timestamp);

/// <summary>
/// A custom-typed message injected by an extension via
/// <c>IPhiApi.SubmitCustomMessage</c>. <see cref="CustomType"/> is the
/// discriminator the host's message renderer (<c>RegisterMessageRenderer</c>)
/// uses to render it; <see cref="Details"/> is opaque structured data for
/// that renderer. Restored to a <see cref="CustomMessage"/> on resume so the
/// transcript replays it (and the provider can map it to an assistant turn).
/// </summary>
public sealed record CustomSessionEntry(
    long Timestamp,
    string CustomType,
    string Content,
    JsonNode? Details = null)
    : SessionEntry(Timestamp);

/// <summary>
/// A namespaced extension entry appended via
/// <c>IPhiApi.AppendEntryAsync</c>. Lives in its own namespace (e.g.
/// <c>"multi-agent:log"</c>) so it doesn't pollute the conversation history;
/// it is persisted for the extension's own bookkeeping and replayed on
/// resume but never becomes an <see cref="IAgentMessage"/>.
/// </summary>
public sealed record ExtensionSessionEntry(
    long Timestamp,
    string Namespace,
    JsonNode? Data = null)
    : SessionEntry(Timestamp);
