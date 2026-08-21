namespace Phi.Sessions;

/// <summary>
/// Owns the <em>current</em> session and its lifecycle. Switching sessions
/// is navigation — <c>/new</c> calls <see cref="NavigateToNewAsync"/>,
/// resuming calls <see cref="ResumeAsync"/> — instead of mutating a single
/// session in place.
/// <para>
/// <see cref="SessionChanged"/> fires after a navigation settles so the UI
/// can rebuild its view. The navigator owns the lifecycle of every session
/// it hands out: the previous session is cancelled, awaited, and disposed
/// when navigating away. Disposing the navigator disposes the current
/// session.
/// </para>
/// </summary>
public interface ISessionNavigator : IDisposable
{
    /// <summary>The live session for the current view.</summary>
    ISession Current { get; }

    /// <summary>Fired after a navigation settles (old session disposed).</summary>
    event Action? SessionChanged;

    /// <summary>
    /// Navigates to a fresh session. The current session (if running) is
    /// cancelled, awaited, and disposed before the new one takes over.
    /// <paramref name="cwd"/> overrides the working directory for the new
    /// session (defaults to the navigator's configured cwd) — used by the
    /// desktop shell to start a chat in a chosen workspace.
    /// </summary>
    Task NavigateToNewAsync(string? cwd = null);

    /// <summary>
    /// Resumes an indexed session by id. The current session is cancelled,
    /// awaited, and disposed before the resumed one takes over. Throws
    /// <see cref="InvalidOperationException"/> for an unknown id (the current
    /// session is left untouched).
    /// </summary>
    Task ResumeAsync(string sessionId);

    /// <summary>Indexed sessions of the current project, newest first.</summary>
    IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7);
}
