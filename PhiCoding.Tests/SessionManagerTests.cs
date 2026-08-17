namespace PhiCoding.Tests;

/// <summary>
/// <see cref="SessionManager"/> owns the per-project session index and all
/// decisions about <em>when</em> a session is persisted. The key contract:
/// <see cref="SessionManager.PrepareSession"/> allocates an id without
/// touching disk; only <see cref="SessionManager.CreateSession"/> /
/// <see cref="SessionManager.Upsert"/> write the index.
/// </summary>
[NotInParallel("session-tests")]
public class SessionManagerTests : IDisposable
{
    private readonly string _cwd;
    private readonly string _phiHome;
    private readonly string _previousPhiHome;

    public SessionManagerTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), "phi-mgr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
        _phiHome = Path.Combine(Path.GetTempPath(), "phi-home-" + Guid.NewGuid().ToString("N"));
        // Tests point SessionPaths.PhiHome at a per-test temp dir; the
        // previous PHI_HOME env-var override is gone now that we keep all
        // filesystem state in the file-based credential / settings files.
        _previousPhiHome = SessionPaths.PhiHome;
        SessionPaths.PhiHome = _phiHome;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SessionPaths.PhiHome = _previousPhiHome;
        if (Directory.Exists(_cwd)) Directory.Delete(_cwd, recursive: true);
        if (Directory.Exists(_phiHome)) Directory.Delete(_phiHome, recursive: true);
    }

    private string IndexFile => SessionPaths.IndexFileFor(_cwd);
    private string ProjectRoot => SessionPaths.SessionRootFor(_cwd);

    // ──────────────────── PrepareSession (lazy) ────────────────────

    [Test]
    public async Task PrepareSession_WritesNothingToDisk()
    {
        var manager = new SessionManager(_cwd);

        var record = manager.PrepareSession("test-model");

        await Assert.That(record.Id).IsNotEmpty();
        await Assert.That(record.Cwd).IsEqualTo(_cwd);
        await Assert.That(record.Model).IsEqualTo("test-model");
        await Assert.That(File.Exists(IndexFile)).IsFalse();
        await Assert.That(Directory.Exists(ProjectRoot)).IsFalse();
    }

    [Test]
    public async Task PrepareSession_CalledTwice_GeneratesUniqueIds()
    {
        var manager = new SessionManager(_cwd);

        var a = manager.PrepareSession("m");
        var b = manager.PrepareSession("m");

        await Assert.That(a.Id).IsNotEqualTo(b.Id);
    }

    [Test]
    public async Task PrepareSession_IsNotListed()
    {
        var manager = new SessionManager(_cwd);
        manager.PrepareSession("m");

        await Assert.That(manager.ListSessions()).IsEmpty();
    }

    // ──────────────────── CreateSession (eager) ────────────────────

    [Test]
    public async Task CreateSession_WritesIndexRecord()
    {
        var manager = new SessionManager(_cwd);

        var record = manager.CreateSession("m", title: "hello");

        await Assert.That(File.Exists(IndexFile)).IsTrue();
        var found = manager.FindSession(record.Id);
        await Assert.That(found).IsNotNull();
        await Assert.That(found!.Title).IsEqualTo("hello");
    }

    [Test]
    public async Task CreateSession_IsListed()
    {
        var manager = new SessionManager(_cwd);
        manager.CreateSession("m");

        var all = manager.ListSessions();
        await Assert.That(all.Count).IsEqualTo(1);
    }

    // ──────────────────── Get / Find ────────────────────

    [Test]
    public async Task GetSession_MissingId_Throws()
    {
        var manager = new SessionManager(_cwd);

        await Assert.That(() => manager.GetSession("nope"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FindSession_MissingId_ReturnsNull()
    {
        var manager = new SessionManager(_cwd);

        await Assert.That(manager.FindSession("nope")).IsNull();
    }

    // ──────────────────── ListSessions ────────────────────

    [Test]
    public async Task ListSessions_OrdersByUpdatedAtDescending()
    {
        var manager = new SessionManager(_cwd);
        var old = manager.CreateSession("m");
        Thread.Sleep(5);
        var newer = manager.CreateSession("m");

        var all = manager.ListSessions();

        await Assert.That(all.Select(r => r.Id)).IsEquivalentTo([newer.Id, old.Id]);
    }

    [Test]
    public async Task ListSessions_FiltersByDayWindow()
    {
        var manager = new SessionManager(_cwd);
        var recent = manager.CreateSession("m");
        var stale = manager.CreateSession("m");
        var staleCutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        manager.Upsert(stale with { UpdatedAt = staleCutoff });

        var all = manager.ListSessions(days: 7);

        await Assert.That(all.Select(r => r.Id)).IsEquivalentTo([recent.Id]);
    }

    // ──────────────────── Upsert ────────────────────

    [Test]
    public async Task Upsert_ExistingId_ReplacesRecord()
    {
        var manager = new SessionManager(_cwd);
        var record = manager.CreateSession("m", title: "old");

        manager.Upsert(record with { Title = "new" });

        await Assert.That(manager.ListSessions().Count).IsEqualTo(1);
        await Assert.That(manager.GetSession(record.Id).Title).IsEqualTo("new");
    }

    [Test]
    public async Task Upsert_UnknownId_AppendsRecord()
    {
        var manager = new SessionManager(_cwd);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        manager.Upsert(new SessionRecord("manual", _cwd, "m", null, now, now));

        await Assert.That(manager.FindSession("manual")).IsNotNull();
    }

    // ──────────────────── Default session ────────────────────

    [Test]
    public async Task GetOrCreateDefaultSession_FirstCall_CreatesIndexedRecord()
    {
        var manager = new SessionManager(_cwd);

        var record = manager.GetOrCreateDefaultSession("m");

        await Assert.That(record.Id).IsEqualTo(SessionPaths.DefaultSessionId(_cwd));
        await Assert.That(manager.FindSession(record.Id)).IsNotNull();
    }

    [Test]
    public async Task GetOrCreateDefaultSession_SecondCall_ReturnsExisting()
    {
        var manager = new SessionManager(_cwd);

        var first = manager.GetOrCreateDefaultSession("m");
        var second = manager.GetOrCreateDefaultSession("other-model");

        await Assert.That(second.Id).IsEqualTo(first.Id);
        await Assert.That(second.Model).IsEqualTo("m");
    }

    // ──────────────────── Paths ────────────────────

    [Test]
    public async Task SessionFileFor_LandsUnderProjectRoot()
    {
        var manager = new SessionManager(_cwd);
        var record = manager.PrepareSession("m");

        var path = manager.SessionFileFor(record.Id);

        await Assert.That(path).IsEqualTo(
            Path.Combine(ProjectRoot, $"{record.Id}.jsonl"));
    }
}
