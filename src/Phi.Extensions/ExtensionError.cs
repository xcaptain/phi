namespace Phi.Extensions;

/// <summary>
/// Thrown by the host (or by <see cref="IPhiApi"/> implementations) when an
/// extension violates an invariant: stale generation after <c>/reload</c>,
/// session not yet bound when an action method fires, capability mismatch,
/// unknown tool name, etc. <see cref="Message"/> is written verbatim to
/// the per-extension audit log and surfaced in the status bar — never
/// silently swallowed by the host.
/// </summary>
public sealed class ExtensionError : Exception
{
    public ExtensionError(string message) : base(message) { }
    public ExtensionError(string message, Exception inner) : base(message, inner) { }
}
