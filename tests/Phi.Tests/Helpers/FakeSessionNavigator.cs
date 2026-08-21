using Phi.Sessions;

namespace Phi.Tests.Helpers;

/// <summary>
/// In-memory <see cref="ISessionNavigator"/> for TUI tests. Wraps a single
/// <see cref="MockSession"/> (or any <see cref="ISession"/>); navigation
/// simply re-raises <see cref="SessionChanged"/>.
/// </summary>
public sealed class FakeSessionNavigator : ISessionNavigator
{
    public FakeSessionNavigator(ISession session)
    {
        Current = session;
    }

    public ISession Current { get; private set; }

    public event Action? SessionChanged;

    /// <summary>Last session id passed to <see cref="ResumeAsync"/>, if any.</summary>
    public string? LastResumedId { get; private set; }

    /// <summary>Number of times <see cref="NavigateToNewAsync"/> was called.</summary>
    public int NavigateToNewCalls { get; private set; }

    /// <summary>Last cwd passed to <see cref="NavigateToNewAsync"/>, if any.</summary>
    public string? LastNewCwd { get; private set; }

    public IReadOnlyList<SessionRecord> RecentSessions { get; set; } = [];

    public Task NavigateToNewAsync(string? cwd = null)
    {
        NavigateToNewCalls++;
        LastNewCwd = cwd;
        SessionChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string sessionId)
    {
        LastResumedId = sessionId;
        SessionChanged?.Invoke();
        return Task.CompletedTask;
    }

    public IReadOnlyList<SessionRecord> ListRecentSessions(int days = 7) => RecentSessions;

    public void Dispose() { }
}
