namespace Phi.Extensions;

/// <summary>
/// Phi extension entry point. The assembly loader (Sprint 1) finds the
/// class annotated with <see cref="PhiExtensionAttribute"/>, instantiates
/// it, and calls <see cref="Setup"/> exactly once per loaded extension.
/// <para>
/// <see cref="Setup"/> is intentionally synchronous: async setup invites
/// race conditions (the session isn't bound yet; action methods need a
/// bound session). Long-running work belongs inside event handlers
/// (registered via <see cref="IPhiApi.On(string, Func{Events.PhiEvent, IPhiContext, ValueTask})"/>)
/// not in <see cref="Setup"/>.
/// </para>
/// </summary>
public interface IPhiExtension
{
    /// <summary>
    /// Called once when the extension is loaded. Register tools, slash
    /// commands, event handlers, transcript-line renderers here. Do not
    /// call action methods on <paramref name="api"/> from within this
    /// method — the session is not yet bound and they will throw
    /// <see cref="ExtensionError"/>.
    /// </summary>
    void Setup(IPhiApi api);
}
