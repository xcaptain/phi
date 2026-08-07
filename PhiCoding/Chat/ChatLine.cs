using PhiCoding.ToolCards;

namespace PhiCoding.Chat;

/// <summary>
/// UI-agnostic projection of one chat line. The <see cref="ChatTranscriptProjector"/>
/// owns a list of these; both the TUI and the desktop UI render the same list
/// into their own control trees (XenoAtom.Visual / MewUI.FrameworkElement).
/// <para>
/// Every line carries a stable <see cref="Id"/> assigned by the projector at
/// creation time so a renderer can DIFF a new projection against its previous
/// visual tree without rebuilding unchanged lines.
/// </para>
/// </summary>
public abstract record ChatLine(string Id);

// ──────── User side ────────

/// <summary>A plain user message ("You") bubble.</summary>
public sealed record UserTextLine(string Id, string Text) : ChatLine(Id);

/// <summary>
/// A user message that was actually a loaded <c>/skill:NAME</c> invocation —
/// the projector parsed the XML payload via
/// <see cref="PhiCoding.Resources.SkillInvocation.TryParse"/> and split the
/// skill body off so a renderer can show a collapsible skill card instead of
/// raw XML.
/// </summary>
public sealed record SkillInvocationLine(string Id, string SkillName, string Body, string? TrailingPrompt) : ChatLine(Id);

/// <summary>
/// Boundary marker for a previously compacted conversation. The projector
/// detects <see cref="ContextWindow.CompactionSummaryPrefix"/> on a
/// <c>UserMessage</c> and turns it into a divider instead of a user turn.
/// </summary>
public sealed record CompactionDividerLine(string Id, string SummaryLine) : ChatLine(Id);

// ──────── Assistant side ────────

/// <summary>
/// Model's chain-of-thought. <see cref="IsStreaming"/> is <c>true</c> while
/// thinking deltas are arriving; <see cref="Duration"/> is filled in when
/// <see cref="PhiAgent.AssistantThinkingEndEvent"/> lands.
/// </summary>
public sealed record ThinkingLine(
    string Id,
    string Text,
    TimeSpan? Duration,
    bool IsStreaming) : ChatLine(Id);

/// <summary>
/// Streamed assistant text. The projector keeps the raw text in <see cref="Text"/>
/// (no markdown parsing) and the renderer formats it however it likes.
/// </summary>
public sealed record AssistantTextLine(string Id, string Text, bool IsStreaming) : ChatLine(Id);

// ──────── Tool side ────────

/// <summary>Lifecycle of a tool call from the projector's point of view.</summary>
public enum ToolResultState
{
    /// <summary>The model emitted the call; the harness hasn't returned a result yet.</summary>
    Pending,
    /// <summary>The harness completed the call without error.</summary>
    Completed,
    /// <summary>The harness reported <see cref="PhiAgent.ToolResult.IsError"/>.</summary>
    Failed,
}

/// <summary>
/// One tool call. <see cref="ArgumentsJson"/> and <see cref="DetailsJson"/>
/// are JSON serializations of the arguments blob and the result details
/// blob respectively; renderers that want a typed view re-deserialize.
/// <see cref="PhiCoding.ToolCards.ToolDescriptors.For"/> maps the tool name
/// to display metadata (title + icon key).
/// </summary>
public sealed record ToolCallLine(
    string Id,
    string ToolCallId,
    string ToolName,
    ToolDescriptor Descriptor,
    string ArgumentsJson,
    ToolResultState ResultState,
    string? ResultText,
    string? DetailsJson) : ChatLine(Id);

// ──────── Errors ────────

/// <summary>
/// Persistent error line added to the transcript. Dedup is the router's job
/// (see <see cref="PhiCoding.Status.SessionStatusRouter"/>); the projector
/// just appends every <see cref="PhiAgent.HarnessErrorEvent"/> it sees and
/// every <c>LastError</c> snapshot that arrives with a non-null value.
/// </summary>
public sealed record PersistentErrorLine(string Id, string Message) : ChatLine(Id);
