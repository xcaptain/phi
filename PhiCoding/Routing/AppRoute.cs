namespace PhiCoding.Routing;

/// <summary>
/// Root application route, in the style of web paths. Each route <em>family</em>
/// maps to exactly one page via <see cref="PhiCoding.Tui.Pages.PageRegistry"/>. Discriminated-union
/// records keep routing exhaustively matchable (no string parsing).
/// </summary>
public abstract record AppRoute;

/// <summary>
/// The chat screen: <c>/sessions/new</c> and <c>/sessions/:id</c>. The
/// <see cref="SessionRequest"/> carries what distinguishes the two paths.
/// </summary>
public sealed record ChatRoute(SessionRequest Request) : AppRoute;

/// <summary>Which session a <see cref="ChatRoute"/> targets.</summary>
public abstract record SessionRequest;

/// <summary>Route for a fresh, unpersisted session (<c>/sessions/new</c>).</summary>
public sealed record NewSessionRequest : SessionRequest;

/// <summary>Route for an indexed session by id (<c>/sessions/:id</c>).</summary>
public sealed record ExistingSessionRequest(string SessionId) : SessionRequest;
