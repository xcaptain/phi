using Phi.Agent;

namespace Phi.Tests;

/// <summary>
/// <see cref="WorkspaceSessionStore"/>: merges every project's session index
/// into a global view so a frontend not bound to one working directory can
/// list sessions across workspaces and resolve a session's cwd.
/// </summary>
[NotInParallel("session-tests")]
public class WorkspaceSessionStoreTests : IDisposable
{
    private readonly string _phiHome;
    private readonly string _previousPhiHome;
    private readonly string _cwdA;
    private readonly string _cwdB;

    public WorkspaceSessionStoreTests()
    {
        _phiHome = Path.Combine(Path.GetTempPath(), $"phi-ws-store-{Guid.NewGuid():N}");
        _cwdA = Path.Combine(Path.GetTempPath(), $"phi-ws-a-{Guid.NewGuid():N}");
        _cwdB = Path.Combine(Path.GetTempPath(), $"phi-ws-b-{Guid.NewGuid():N}");
        _previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = _phiHome;
        Directory.CreateDirectory(_cwdA);
        Directory.CreateDirectory(_cwdB);
    }

    public void Dispose()
    {
        SessionPaths.PhiHome = _previousPhiHome;
        foreach (var dir in new[] { _cwdA, _cwdB, _phiHome })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static Session Persist(string cwd, string id, string text)
    {
        var session = Session.Create(cwd, "m");
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

    // ──────── Rename / Delete ────────

    [Test]
    public async Task RenameSession_UpdatesIndexTitle()
    {
        var a = Persist(_cwdA, "a", "hello");

        WorkspaceSessionStore.RenameSession(a.Id, "New title");

        var found = WorkspaceSessionStore.FindSession(a.Id);
        await Assert.That(found).IsNotNull();
        await Assert.That(found!.Title).IsEqualTo("New title");
    }

    [Test]
    public async Task RenameSession_UnknownId_Throws()
    {
        await Assert.That(() => WorkspaceSessionStore.RenameSession("nope", "t"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DeleteSession_RemovesIndexAndTranscript()
    {
        var a = Persist(_cwdA, "a", "hello");

        WorkspaceSessionStore.DeleteSession(a.Id);

        await Assert.That(WorkspaceSessionStore.FindSession(a.Id)).IsNull();
        // The transcript file is gone too.
        var file = SessionPaths.SessionFileFor(_cwdA, a.Id);
        await Assert.That(File.Exists(file)).IsFalse();
    }

    [Test]
    public async Task DeleteSession_UnknownId_IsNoOp()
    {
        WorkspaceSessionStore.DeleteSession("nope");
        await Assert.That(WorkspaceSessionStore.FindSession("nope")).IsNull();
    }

    [Test]
    public async Task DeleteWorkspace_RemovesAllSessionsInCwd_KeepsOthers()
    {
        var a1 = Persist(_cwdA, "a1", "one");
        var a2 = Persist(_cwdA, "a2", "two");
        var b = Persist(_cwdB, "b", "three");

        WorkspaceSessionStore.DeleteWorkspace(_cwdA);

        await Assert.That(WorkspaceSessionStore.FindSession(a1.Id)).IsNull();
        await Assert.That(WorkspaceSessionStore.FindSession(a2.Id)).IsNull();
        await Assert.That(WorkspaceSessionStore.FindSession(b.Id)).IsNotNull();
    }
}
