using PhiCoding.Routing;

namespace PhiCoding.Sessions;

/// <summary>
/// Routes sessions for the app. Owns the <em>current</em> session and its
/// lifecycle; switching sessions is navigation — <c>/new</c> navigates to a
/// <see cref="ChatRoute"/> carrying <see cref="NewSessionRequest"/>, resuming
/// navigates to one carrying <see cref="ExistingSessionRequest"/> — instead
/// of mutating a single session in place.
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
    AppRoute Route { get; }

    /// <summary>Fired after a navigation settles (old session disposed).</summary>
    event Action<AppRoute>? RouteChanged;

    /// <summary>
    /// Navigates to a route, building the target session, cancelling +
    /// awaiting any in-flight run on the current session, disposing the
    /// outgoing session, and swapping <see cref="Current"/>/<see cref="Route"/>.
    /// Navigating to the current session's own id (a new-session page
    /// promoting to its detail route) is a no-op adoption: the session is
    /// neither rebuilt, cancelled, nor disposed.
    /// Throws <see cref="InvalidOperationException"/> when an
    /// <see cref="ExistingSessionRequest"/> id is unknown (the current session
    /// is left untouched).
    /// </summary>
    Task NavigateAsync(AppRoute route);

    /// <summary>
    /// Carries the first prompt submitted on the new-session page to the
    /// session page so its transcript can render the user bubble (the run is
    /// already in flight). Set before promoting; consumed (and cleared) by the
    /// session page on mount.
    /// </summary>
    void SetPendingSubmission(string text);

    /// <summary>Returns and clears the pending submission, if any.</summary>
    string? TakePendingSubmission();

    /// <summary>Indexed sessions of the current project, newest first.</summary>
    IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7);
}
