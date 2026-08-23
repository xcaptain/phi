namespace Phi.Extensions;

/// <summary>
/// Routing hint for <see cref="IPhiApi.SubmitUserMessage"/> and
/// <see cref="IPhiApi.SubmitCustomMessage"/>. Mirrors tau's
/// <c>MessageDelivery</c> split between steer (next-turn immediate) and
/// follow_up (after the next turn ends cleanly).
/// </summary>
public enum MessageDelivery
{
    /// <summary>
    /// Inject the message at the start of the next iteration, before the
    /// turn counter advances. Skips a <c>TurnStartEvent</c> for the empty
    /// iteration. Use for "actually redirect what you're doing".
    /// </summary>
    Steer,

    /// <summary>
    /// Queue the message until the current turn ends naturally (no tool
    /// calls), then start a new turn with it. Use for "after you finish,
    /// also do X".
    /// </summary>
    FollowUp,
}
