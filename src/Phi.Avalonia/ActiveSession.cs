namespace Phi.Avalonia;

/// <summary>
/// Holds the currently active <see cref="ISession"/> for the desktop
/// shell and exposes a <see cref="Changed"/> event so the chat page can
/// rebuild on navigation. Equivalent in role to XenoAtom's
/// <c>State&lt;ISession&gt;</c> for the TUI; needed because Avalonia has
/// no built-in reactive primitive of its own.
/// <para>
/// Pure UI binding helper — no session-construction logic. The session
/// itself owns navigation (<see cref="ISession.NewSessionAsync"/> /
/// <see cref="ISession.ResumeAsync"/>); the holder just stores the
/// reference and notifies subscribers when the user navigates inside the
/// chat.
/// </para>
/// </summary>
public sealed class ActiveSession
{
    private ISession _current;

    /// <summary>The session the chat page is currently bound to.</summary>
    public ISession Current => _current;

    /// <summary>
    /// Fires after <see cref="Replace"/> swaps the active session. The
    /// shell subscribes and rebuilds the chat page against the new
    /// session.
    /// </summary>
    public event Action? Changed;

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
}
