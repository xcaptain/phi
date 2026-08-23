namespace Phi.Extensions.Events;

// ──────── Session lifecycle ────────

/// <summary>
/// Reason the session started / shut down — useful for extensions that
/// need to distinguish "fresh startup" from "user typed /new" from "user
/// pressed /reload".
/// </summary>
public enum SessionLifecycleReason
{
    Startup,
    New,
    Resume,
    Reload,
    Quit,
}

/// <summary>
/// Fired after the session is fully constructed (model bound, system
/// prompt set, tools registered). Handlers can assume
/// <see cref="IPhiContext.Ui"/> is non-null and the model is ready.
/// </summary>
public sealed record SessionStartEvent(SessionLifecycleReason Reason) : PhiEvent;

/// <summary>
/// Fired just before the session is disposed. Handlers should flush any
/// pending state; after this event, <see cref="IPhiContext"/> members may
/// throw <see cref="ExtensionError"/> on access.
/// </summary>
public sealed record SessionShutdownEvent(SessionLifecycleReason Reason) : PhiEvent;

// ──────── Project trust ────────

/// <summary>Decision a project-extension trust prompt elicits.</summary>
public enum ExtensionTrustDecision
{
    Approve,
    Decline,
    Defer,
}

/// <summary>
/// Fired when a project-level extension set would be loaded (Sprint 3+).
/// Built-in / user / explicit extensions can return
/// <see cref="ExtensionTrustResult"/> to vote; the user's vote decides.
/// </summary>
public sealed record ProjectTrustEvent(
    string Cwd,
    bool HasUi,
    IReadOnlyList<string> ProjectExtensionNames) : PhiEvent;

/// <summary>
/// Vote returned by a project-trust handler. <c>Remember = true</c>
/// writes the decision to <c>~/.phi/config.json</c> so the user isn't
/// re-prompted.
/// </summary>
public sealed record ExtensionTrustResult(
    ExtensionTrustDecision Decision,
    bool Remember);
