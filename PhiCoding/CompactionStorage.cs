using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Performs an in-place compaction rewrite of a session's jsonl transcript
/// and its in-memory harness. Phi's storage is linear (no entry tree), so
/// "compaction" means: clear the file, append one
/// <see cref="CompactionSessionEntry"/> summarizing the dropped prefix,
/// append entries for every kept message, then call
/// <see cref="Harness.ReplaceMessages"/> on the live harness and reset the
/// session's flush watermark so the next flush doesn't re-append the kept
/// messages.
/// <para>
/// Order is critical: write file first, then mutate in-memory state. The
/// reverse would risk a crash window where in-memory and disk diverge.
/// </para>
/// </summary>
public static class CompactionStorage
{
    public static async Task RewriteAsync(
        CodingSession session,
        CompactionPlan plan,
        string summary,
        int tokensBefore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(summary);

        var compaction = new CompactionSessionEntry(
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Summary: summary,
            TokensBefore: tokensBefore);

        var entryByMessage = new Dictionary<IAgentMessage, SessionEntry>();
        foreach (var m in plan.KeptMessages)
            entryByMessage[m] = SessionEntryConverter.FromAgentMessage(m);

        // 1. Clear and rewrite the file. We can't await inside SessionStorage's
        //    lock, so serialize the work and run it on a thread-pool hop.
        await Task.Run(() =>
        {
            session.Storage.Clear();
            session.Storage.Append(compaction);
            foreach (var m in plan.KeptMessages)
                session.Storage.Append(entryByMessage[m]);
        }, cancellationToken).ConfigureAwait(false);

        // 2. Rebuild the in-memory message list with a UserMessage prefix
        //    carrying the summary text, so the runtime sees the compacted
        //    history as ordinary user-role content (the provider layer stays
        //    ignorant of any compaction-specific message type).
        var newMessages = new List<IAgentMessage>
        {
            new UserMessage
            {
                Content = ContextWindow.CompactionSummaryPrefix + summary,
                Timestamp = compaction.Timestamp,
            },
        };
        newMessages.AddRange(plan.KeptMessages);

        session.ReplaceMessagesForCompaction(newMessages);
    }
}