namespace Phi.Tests.Chat;

/// <summary>
/// <c>NotInParallel</c> keys for tests touching the chat projector and the
/// session-event projection pipeline. The projector itself is per-instance
/// and stateless across tests, but the underlying <c>MockSession</c> events
/// fire synchronously — keep this group small so it doesn't serialize the
/// full TUI suite.
/// </summary>
internal static class ChatTestGroups
{
    /// <summary>Projector lifecycle, replay, and dedup tests.</summary>
    public const string Projector = "phicoding-chat-projector-tests";
}
