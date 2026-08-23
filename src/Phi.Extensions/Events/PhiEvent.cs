namespace Phi.Extensions.Events;

/// <summary>
/// Marker interface for every event payload record passed to
/// <see cref="IPhiApi.On"/>. Concrete payloads are <c>sealed record</c>s
/// (e.g. <see cref="AgentStartEvent"/>) declared in this namespace.
/// <para>
/// Sprint 0 declares the marker only. Individual payloads land in Sprint 1
/// (Agent / Message / ToolExecution events) and Sprint 2 (Hook /
/// Lifecycle events) — the host's <c>ExtensionRuntime</c> translates from
/// <c>ISession.StateChanged</c> / <c>ISession.HarnessEvent</c> into typed
/// <see cref="PhiEvent"/> instances and dispatches to <c>On(...)</c>
/// handlers. Extensions don't see Phi.Agent types directly through this
/// surface; the translation layer is the only place that names them.
/// </para>
/// </summary>
public interface PhiEvent;
