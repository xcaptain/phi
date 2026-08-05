namespace PhiCoding.Sessions;

/// <summary>
/// A typed application route, in the style of <c>/sessions/new</c> and
/// <c>/sessions/:id</c>. The <see cref="SessionNavigator"/> maps a route to
/// a live <see cref="CodingSession"/>; the TUI renders the page for the
/// current route. Discriminated-union records keep routing exhaustively
/// matchable (no string-parsing).
/// </summary>
public abstract record SessionRoute;

/// <summary>Route for a fresh, unpersisted session (<c>/sessions/new</c>).</summary>
public sealed record NewSessionRoute : SessionRoute;

/// <summary>Route for an indexed session by id (<c>/sessions/:id</c>).</summary>
public sealed record ExistingSessionRoute(string SessionId) : SessionRoute;
