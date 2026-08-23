namespace Phi.Extensions.Events;

// ──────── Queue updates ────────

/// <summary>
/// Fired when the steering or follow-up queue count changes
/// (extension can render a "3 queued" badge in the status bar).
/// </summary>
public sealed record QueueUpdateEvent(
    int SteeringCount,
    int FollowUpCount) : PhiEvent;

// ──────── Compaction ────────

/// <summary>Fired before auto-compaction begins.</summary>
public sealed record CompactionStartEvent(string Reason) : PhiEvent;

/// <summary>
/// Fired after auto-compaction finishes. <c>Aborted</c> is true if the
/// compaction was cancelled mid-flight; <c>WillRetry</c> is true if the
/// host scheduled a retry (Phi v1 doesn't, but the field is here for
/// forward compatibility).
/// </summary>
public sealed record CompactionEndEvent(
    string Reason,
    Phi.Agent.CompactionDetails? Result,
    bool Aborted,
    bool WillRetry,
    string? ErrorMessage) : PhiEvent;

// ──────── Entry appended ────────

/// <summary>
/// Fired after a <c>SessionEntry</c> has been appended to the session
/// transcript (and therefore persisted to the JSONL file). <c>Entry</c>
/// is the concrete <c>SessionEntry</c> subclass — handlers can pattern
/// match on type (UserMessageEntry, AssistantMessageEntry, etc.).
/// </summary>
public sealed record EntryAppendedEvent(
    Phi.Agent.SessionEntry Entry) : PhiEvent;
