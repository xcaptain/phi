using Phi.Agent;
using Phi.Extensions.Events;

namespace Phi.Extensions.Host;

/// <summary>
/// Translates <see cref="Phi.Session"/>'s two event streams —
/// <see cref="Phi.Session.HarnessEvent"/> (per-turn records) and
/// <see cref="Phi.Session.StateChanged"/> (immutable state snapshots) —
/// into typed <see cref="PhiEvent"/> payloads, then dispatches each to
/// handlers registered via <c>api.On("&lt;TypeName&gt;", handler)</c>.
/// <para>
/// <c>eventName</c> matches the <see cref="PhiEvent"/> runtime type name
/// (e.g. <c>"TurnStartEvent"</c>, <c>"AgentStartEvent"</c>). Sprint 2
/// implements the lifecycle subset; streaming message events
/// (<c>MessageUpdateEvent</c>) land in Sprint 4 when the harness streams
/// assistant content through a projector.
/// </para>
/// </summary>
internal sealed class EventDispatch : IDisposable
{
    private readonly Dictionary<string, List<RegisteredHandler>> _handlers = [];
    private readonly Phi.Session _session;
    private bool _disposed;

    /// <summary>A handler with its owning extension (for staleness + audit).</summary>
    private sealed record RegisteredHandler(LoadedExtension Extension, Func<PhiEvent, IPhiContext, ValueTask> Handler);

    public EventDispatch(Phi.Session session)
    {
        _session = session;
        session.HarnessEvent += OnHarnessEvent;
        session.StateChanged += OnStateChanged;
    }

    public IDisposable Register(string eventName, LoadedExtension ext, Func<PhiEvent, IPhiContext, ValueTask> handler)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EventDispatch));

        var list = _handlers.TryGetValue(eventName, out var existing)
            ? existing
            : (_handlers[eventName] = []);
        var registered = new RegisteredHandler(ext, handler);
        list.Add(registered);
        return new Unregister(list, registered);
    }

    private sealed class Unregister(List<RegisteredHandler> list, RegisteredHandler h) : IDisposable
    {
        public void Dispose() => list.Remove(h);
    }

    private void Dispatch<TEvent>(TEvent ev) where TEvent : PhiEvent
    {
        var eventName = typeof(TEvent).Name;
        if (!_handlers.TryGetValue(eventName, out var handlers)) return;

        // Snapshot so handlers can register/unregister mid-dispatch.
        foreach (var h in handlers.ToArray())
        {
            try { h.Handler(ev, new PhiContextForEvent(_session)).AsTask().GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                // A handler throwing must not break the session loop.
                _session.AddExtensionPromptGuideline($"hook {eventName} failed: {ex.Message}");
            }
        }
    }

    // ──────── HarnessEvent → PhiEvent ────────

    private int _lastTurn;

    private void OnHarnessEvent(HarnessEvent ev)
    {
        switch (ev)
        {
            case Phi.Agent.TurnStartEvent t:
                _lastTurn = t.Turn;
                Dispatch(new Phi.Extensions.Events.TurnStartEvent(t.Turn, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                break;
            case Phi.Agent.TurnEndEvent t:
                Dispatch(new Phi.Extensions.Events.TurnEndEvent(_lastTurn, t.FinalMessage, []));
                break;
            case Phi.Agent.ToolExecutionStartEvent t:
                Dispatch(new Phi.Extensions.Events.ToolExecutionStartEvent(t.ToolCallId, t.ToolName, new System.Text.Json.Nodes.JsonObject()));
                break;
            case Phi.Agent.ToolExecutionEndEvent t:
                Dispatch(new Phi.Extensions.Events.ToolExecutionEndEvent(t.ToolCall.Id, t.ToolCall.Name, t.Result));
                break;
            case HarnessErrorEvent e:
                // No public PhiEvent payload for harness errors yet; skip.
                break;
        }
    }

    // ──────── StateChanged → PhiEvent ────────

    private SessionState? _lastState;

    private void OnStateChanged(SessionState state)
    {
        var prev = _lastState;
        _lastState = state;

        if (prev is not null)
        {
            if (!prev.IsRunning && state.IsRunning)
                Dispatch(new AgentStartEvent());
            if (prev.IsRunning && !state.IsRunning)
                Dispatch(new AgentEndEvent(state.Messages, WillRetry: false));

            if (prev.SteeringCount != state.SteeringCount || prev.FollowUpCount != state.FollowUpCount)
                Dispatch(new QueueUpdateEvent(state.SteeringCount, state.FollowUpCount));

            if (!string.Equals(prev.Model, state.Model, StringComparison.Ordinal)
                || !string.Equals(prev.ProviderName, state.ProviderName, StringComparison.Ordinal)
                || !string.Equals(prev.SessionTitle, state.SessionTitle, StringComparison.Ordinal))
            {
                Dispatch(new SessionInfoChangedEvent(
                    state.SessionId, state.SessionTitle, state.Model, state.ProviderName));
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.HarnessEvent -= OnHarnessEvent;
        _session.StateChanged -= OnStateChanged;
        _handlers.Clear();
    }

    /// <summary>Read-only context passed to handlers — shares the session state.</summary>
    private sealed class PhiContextForEvent(Phi.Session session) : IPhiContext
    {
        public string Cwd => session.Cwd;
        public string Model => session.State.Model;
        public string ProviderName => session.State.ProviderName;
        public string SessionId => session.Id;
        public string SystemPrompt => session.SystemPrompt;
        public bool IsRunning => session.State.IsRunning;
        public bool HasUi => session.HasUi;
        public IReadOnlyList<IAgentMessage> Transcript => session.State.Messages;
        public IPhiUiBridge Ui { get; } = new NullPhiUiBridge();
    }
}
