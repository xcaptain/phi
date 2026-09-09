using Phi;

namespace Phi.Avalonia.Desktop;

/// <summary>
/// Holds the currently active <see cref="ISession"/> for the desktop
/// shell and exposes a <see cref="Changed"/> event so the chat page can
/// rebuild on navigation. Equivalent in role to XenoAtom's
/// <c>State&lt;ISession&gt;</c> for the TUI; Avalonia has no built-in
/// reactive primitive so we ship this thin wrapper.
/// <para>
/// Pure UI binding helper — no session-construction logic. The session
/// itself owns navigation (<see cref="ISession.NewSessionAsync"/> /
/// <see cref="ISession.ResumeAsync"/>); the holder just stores the
/// reference and notifies subscribers when the user navigates inside the
/// chat.
/// </para>
/// <para>
/// <see cref="Current"/> is nullable: the shell can boot with no active
/// session (the pre-session prompt-input state) and a session is created
/// on first message submit. Subscribers should null-check before reading
/// <see cref="Current.State"/>.
/// </para>
/// </summary>
public sealed class ActiveSession
{
    private ISession? _current;

    /// <summary>The session the chat page is currently bound to. Null
    /// when the shell is in pre-session state (no chat started yet).</summary>
    public ISession? Current => _current;

    /// <summary>
    /// Fires after <see cref="Replace"/> swaps the active session. The
    /// shell subscribes and rebuilds the chat page against the new
    /// session.
    /// </summary>
    public event Action? Changed;

    public ActiveSession() { }

    public ActiveSession(ISession initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = initial;
    }

    /// <summary>
    /// Atomically swaps the active session. Callers typically do
    /// <c>active.Replace(await current.NewSessionAsync(...))</c> or
    /// <c>active.Replace(await current.ResumeAsync(id))</c>. After this
    /// returns, the old session is disposed (by the <see cref="ISession"/>
    /// implementation) and the new one is live.
    /// </summary>
    public void Replace(ISession next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (ReferenceEquals(next, _current)) return;
        _current = next;
        Changed?.Invoke();
    }

    /// <summary>
    /// Drops the active session reference and disposes the outgoing
    /// session so its <see cref="System.Threading.CancellationTokenSource"/>,
    /// provider HTTP transport, and extension runtime are released
    /// promptly. Used when the shell wants to enter pre-session state
    /// for a fresh navigation (sidebar <c>New Chat</c> from an existing
    /// chat) — the holder owns the lifecycle so callers don't have to
    /// pair a Dispose call with every navigation. No-op when the
    /// holder is already empty.
    /// </summary>
    public void Clear()
    {
        if (_current is null) return;
        var outgoing = _current;
        _current = null;
        outgoing.Dispose();
        Changed?.Invoke();
    }
}
