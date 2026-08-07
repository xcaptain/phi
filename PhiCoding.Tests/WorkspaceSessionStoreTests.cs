using PhiAgent;

namespace PhiCoding.Tests;

/// <summary>
/// <see cref="WorkspaceSessionStore"/>: merges every project's session index
/// into a global view so a frontend not bound to one working directory can
/// list sessions across workspaces and resolve a session's cwd.
/// </summary>
[NotInParallel("session-tests")]
public class WorkspaceSessionStoreTests : IDisposable
{
    private readonly string _phiHome;
    private readonly string _cwdA;
    private readonly string _cwdB;

    public WorkspaceSessionStoreTests()
    {
        _phiHome = Path.Combine(Path.GetTempPath(), $"phi-ws-store-{Guid.NewGuid():N}");
        _cwdA = Path.Combine(Path.GetTempPath(), $"phi-ws-a-{Guid.NewGuid():N}");
        _cwdB = Path.Combine(Path.GetTempPath(), $"phi-ws-b-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("PHI_HOME", _phiHome);
        Directory.CreateDirectory(_cwdA);
        Directory.CreateDirectory(_cwdB);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PHI_HOME", null);
        foreach (var dir in new[] { _cwdA, _cwdB, _phiHome })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static CodingSession Persist(string cwd, string id, string text)
    {
        var session = CodingSession.Create(cwd, "m");
        session.AppendMessage(new UserMessage { Content = text });
        // Force a deterministic id via the record (ids are GUIDs; the helper
        // returns the actual id).
        return session;
    }

    [Test]
    public async Task ListAllSessions_MergesAcrossWorkspaces()
    {
        var a = Persist(_cwdA, "a", "in workspace a");
        var b = Persist(_cwdB, "b", "in workspace b");

        var all = WorkspaceSessionStore.ListAllSessions();

        await Assert.That(all.Select(r => r.Id)).Contains(a.Id);
        await Assert.That(all.Select(r => r.Id)).Contains(b.Id);
    }

    [Test]
    public async Task ListAllSessions_EmptyStore_ReturnsEmpty()
    {
        var all = WorkspaceSessionStore.ListAllSessions();

        await Assert.That(all).IsEmpty();
    }

    [Test]
    public async Task ListWorkspaces_ReturnsDistinctCwds()
    {
        Persist(_cwdA, "a1", "one");
        Persist(_cwdA, "a2", "two");
        Persist(_cwdB, "b1", "three");

        var workspaces = WorkspaceSessionStore.ListWorkspaces();

        await Assert.That(workspaces.Count).IsEqualTo(2);
        await Assert.That(workspaces).Contains(Path.GetFullPath(_cwdA));
        await Assert.That(workspaces).Contains(Path.GetFullPath(_cwdB));
    }

    [Test]
    public async Task ListWorkspaces_OrdersByNewestActivity()
    {
        var a = Persist(_cwdA, "a", "older");
        // Touch B later so it's the most recent workspace.
        Thread.Sleep(15);
        var b = Persist(_cwdB, "b", "newer");

        var workspaces = WorkspaceSessionStore.ListWorkspaces();

        await Assert.That(workspaces[0]).IsEqualTo(Path.GetFullPath(b.Cwd));
        await Assert.That(workspaces[1]).IsEqualTo(Path.GetFullPath(a.Cwd));
    }

    [Test]
    public async Task FindSession_AcrossWorkspace_ReturnsRecordWithCwd()
    {
        var session = Persist(_cwdB, "b", "in b");

        var found = WorkspaceSessionStore.FindSession(session.Id);

        await Assert.That(found).IsNotNull();
        await Assert.That(Path.GetFullPath(found!.Cwd)).IsEqualTo(Path.GetFullPath(_cwdB));
    }

    [Test]
    public async Task FindSession_Unknown_ReturnsNull()
    {
        await Assert.That(WorkspaceSessionStore.FindSession("nope")).IsNull();
    }
}
