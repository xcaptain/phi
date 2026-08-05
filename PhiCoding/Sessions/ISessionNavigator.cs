namespace PhiCoding.Sessions;

/// <summary>
/// Routes sessions for the app. Owns the <em>current</em> session and its
/// lifecycle; switching sessions is navigation — <c>/new</c> navigates to
/// <see cref="NewSessionRoute"/>, resuming navigates to
/// <see cref="ExistingSessionRoute"/> — instead of mutating a single session
/// in place.
/// <para>
/// <see cref="RouteChanged"/> fires after a navigation settles so the UI can
/// rebuild its route-bound page. The navigator owns the lifecycle of every
/// session it hands out: the previous session is cancelled, awaited, and
/// disposed when navigating away. Disposing the navigator disposes the
/// current session.
/// </para>
/// </summary>
public interface ISessionNavigator : IDisposable
{
    /// <summary>The live session for the current route.</summary>
    ISession Current { get; }

    /// <summary>The current route.</summary>
    SessionRoute Route { get; }

    /// <summary>Fired after a navigation settles (old session disposed).</summary>
    event Action<SessionRoute>? RouteChanged;

    /// <summary>
    /// Navigates to a route, building the target session, cancelling +
    /// awaiting any in-flight run on the current session, disposing the
    /// outgoing session, and swapping <see cref="Current"/>/<see cref="Route"/>.
    /// Throws <see cref="InvalidOperationException"/> when an
    /// <see cref="ExistingSessionRoute"/> id is unknown (the current session
    /// is left untouched).
    /// </summary>
    Task NavigateAsync(SessionRoute route);

    /// <summary>Indexed sessions of the current project, newest first.</summary>
    IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7);
}
