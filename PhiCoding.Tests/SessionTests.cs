using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiCoding.Tests;

[NotInParallel("session-tests")]
public class SessionTests : IDisposable
{
    // cwd is a unique temp directory that SessionPaths will compute a
    // project key for. PHI_HOME is set to a per-test temp dir so sessions
    // land in isolation.
    private readonly string _cwd;
    private readonly string _phiHome;
    private readonly string _previousPhiHome;

    public SessionTests()
    {
        _cwd = Path.Combine(Path.GetTempPath(), "phi-coding-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cwd);
        _phiHome = Path.Combine(Path.GetTempPath(), "phi-home-" + Guid.NewGuid().ToString("N"));
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

    // ──────────────────── SessionPaths / default id ────────────────────

    [Test]
    public async Task ProjectKey_SamePath_ReturnsSameKey()
    {
        var k1 = SessionPaths.ProjectKey("/tmp/foo");
        var k2 = SessionPaths.ProjectKey("/tmp/foo");

        await Assert.That(k1).IsEqualTo(k2);
    }

    [Test]
    public async Task ProjectKey_DifferentPaths_DifferentKeys()
    {
        var a = SessionPaths.ProjectKey("/tmp/foo");
        var b = SessionPaths.ProjectKey("/tmp/bar");

        await Assert.That(a).IsNotEqualTo(b);
    }

    [Test]
    public async Task DefaultSessionId_SameCwd_ReturnsSameId()
    {
        var id1 = SessionPaths.DefaultSessionId(_cwd);
        var id2 = SessionPaths.DefaultSessionId(_cwd);

        await Assert.That(id1).IsEqualTo(id2);
        await Assert.That(id1).StartsWith("default-");
    }

    // ──────────────────── SessionIndex ────────────────────

    [Test]
    public async Task SessionIndex_ListAll_EmptyWhenNoFile()
    {
        SessionPaths.EnsureRootFor(_cwd);
        var index = new SessionIndex(SessionPaths.IndexFileFor(_cwd));

        await Assert.That(index.ListAll()).IsEmpty();
    }

    [Test]
    public async Task SessionIndex_Upsert_ThenList_ReturnsRecord()
    {
        var index = new SessionIndex(SessionPaths.IndexFileFor(_cwd));
        var record = NewRecord("id1");

        index.Upsert(record);

        var all = index.ListAll();
        await Assert.That(all.Count).IsEqualTo(1);
        await Assert.That(all[0].Id).IsEqualTo("id1");
    }

    [Test]
    public async Task SessionIndex_UpsertSameId_ReplacesNotDuplicates()
    {
        var index = new SessionIndex(SessionPaths.IndexFileFor(_cwd));
        index.Upsert(NewRecord("id1", title: "old"));
        index.Upsert(NewRecord("id1", title: "new"));

        var all = index.ListAll();
        await Assert.That(all.Count).IsEqualTo(1);
        await Assert.That(all[0].Title).IsEqualTo("new");
    }

    [Test]
    public async Task SessionIndex_GetById_ReturnsMatchingRecord()
    {
        var index = new SessionIndex(SessionPaths.IndexFileFor(_cwd));
        index.Upsert(NewRecord("id1", title: "first"));
        index.Upsert(NewRecord("id2", title: "second"));

        await Assert.That(index.Get("id2")?.Title).IsEqualTo("second");
        await Assert.That(index.Get("missing")).IsNull();
    }

    [Test]
    public async Task SessionIndex_ListForCwd_FiltersByCwd()
    {
        var indexA = new SessionIndex(SessionPaths.IndexFileFor(_cwd));
        var cwdB = Path.Combine(Path.GetTempPath(), "proj-b-" + Guid.NewGuid());
        Directory.CreateDirectory(cwdB);
        var indexB = new SessionIndex(SessionPaths.IndexFileFor(cwdB));

        indexA.Upsert(NewRecord("a", cwd: _cwd));
        indexA.Upsert(NewRecord("a2", cwd: _cwd));
        indexB.Upsert(NewRecord("b", cwd: cwdB));

        await Assert.That(indexA.ListAll().Count).IsEqualTo(2);
        await Assert.That(indexB.ListAll().Count).IsEqualTo(1);

        if (Directory.Exists(cwdB)) Directory.Delete(cwdB, recursive: true);
    }

    [Test]
    public async Task SessionIndex_ListAll_OrdersByUpdatedAtDescending()
    {
        var index = new SessionIndex(SessionPaths.IndexFileFor(_cwd));
        index.Upsert(NewRecord("old", updatedAt: 100));
        index.Upsert(NewRecord("new", updatedAt: 200));
        index.Upsert(NewRecord("mid", updatedAt: 150));

        var all = index.ListAll();
        await Assert.That(all.Select(r => r.Id)).IsEquivalentTo(["new", "mid", "old"]);
    }

    // ──────────────────── CodingSession lifecycle ────────────────────

    [Test]
    public async Task GetOrCreateDefault_FirstCall_CreatesNewSession()
    {
        var session = CodingSession.GetOrCreateDefault(_cwd, "test-model");

        await Assert.That(session.Id).IsEqualTo(SessionPaths.DefaultSessionId(_cwd));
        await Assert.That(session.Cwd).IsEqualTo(_cwd);
        await Assert.That(session.Model).IsEqualTo("test-model");
        await Assert.That(session.LoadMessages()).IsEmpty();
    }

    [Test]
    public async Task GetOrCreateDefault_SecondCall_ReturnsSameSession()
    {
        var first = CodingSession.GetOrCreateDefault(_cwd, "test-model");
        var second = CodingSession.GetOrCreateDefault(_cwd, "test-model");

        await Assert.That(second.Id).IsEqualTo(first.Id);
    }

    [Test]
    public async Task Create_GeneratesUniqueIds()
    {
        var a = CodingSession.Create(_cwd, "m");
        var b = CodingSession.Create(_cwd, "m");

        await Assert.That(a.Id).IsNotEqualTo(b.Id);
    }

    [Test]
    public async Task AppendMessage_PersistsAndLoadsBack()
    {
        var session = CodingSession.Create(_cwd, "m");

        session.AppendMessage(new UserMessage { Content = "hello" });
        session.AppendMessage(new AssistantMessage
        {
            Content = [new TextBlock("hi back")],
            StopReason = StopReasons.Stop,
        });
        session.AppendMessage(new ToolResultMessage
        {
            ToolCallId = "c1",
            ToolName = "bash",
            Content = [new TextBlock("ok")],
            IsError = false,
        });

        var reopened = CodingSession.Resume(session.Id, _cwd);
        var loaded = reopened.LoadMessages();

        await Assert.That(loaded.Count).IsEqualTo(3);
        await Assert.That(loaded[0]).IsTypeOf<UserMessage>();
        await Assert.That(((UserMessage)loaded[0]).Text).IsEqualTo("hello");
        await Assert.That(loaded[1]).IsTypeOf<AssistantMessage>();
        await Assert.That(((TextBlock)((AssistantMessage)loaded[1]).Content[0]).Text).IsEqualTo("hi back");
        await Assert.That(loaded[2]).IsTypeOf<ToolResultMessage>();
        var tr = (ToolResultMessage)loaded[2];
        await Assert.That(tr.ToolCallId).IsEqualTo("c1");
        await Assert.That(tr.ToolName).IsEqualTo("bash");
        await Assert.That(tr.IsError).IsFalse();
    }

    [Test]
    public async Task Resume_NonExistentSession_Throws()
    {
        await Assert.That(() => CodingSession.Resume("nope", _cwd))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AppendMessage_UpdatesIndexTimestamp()
    {
        var session = CodingSession.Create(_cwd, "m");
        var initialUpdatedAt = session.Record.UpdatedAt;

        await Task.Delay(5);

        session.AppendMessage(new UserMessage { Content = "x" });

        await Assert.That(session.Record.UpdatedAt).IsGreaterThan(initialUpdatedAt);
    }

    [Test]
    public async Task Touch_BumpsUpdatedAtInIndex()
    {
        var session = CodingSession.Create(_cwd, "m");
        var before = session.Record.UpdatedAt;

        await Task.Delay(5);
        session.Touch();

        await Assert.That(session.Record.UpdatedAt).IsGreaterThan(before);
        var fromIndex = new SessionIndex(SessionPaths.IndexFileFor(_cwd)).Get(session.Id);
        await Assert.That(fromIndex).IsNotNull();
        await Assert.That(fromIndex!.UpdatedAt).IsEqualTo(session.Record.UpdatedAt);
    }

    [Test]
    public async Task Rename_UpdatesTitleInIndex()
    {
        var session = CodingSession.Create(_cwd, "m");

        session.Rename("My Cool Session");

        var fromIndex = new SessionIndex(SessionPaths.IndexFileFor(_cwd)).Get(session.Id);
        await Assert.That(fromIndex?.Title).IsEqualTo("My Cool Session");
        await Assert.That(session.Record.Title).IsEqualTo("My Cool Session");
    }

    [Test]
    public async Task AppendMessage_ToolCallArgsRoundTrip()
    {
        var session = CodingSession.Create(_cwd, "m");
        var args = JsonNode.Parse("""{"command":"ls","flags":["-la","-h"]}""")!.AsObject();

        session.AppendMessage(new AssistantMessage
        {
            Content = [new ToolCall("c1", "bash") { Arguments = args }],
            StopReason = StopReasons.ToolUse,
        });

        var loaded = CodingSession.Resume(session.Id, _cwd).LoadMessages();
        var assistant = (AssistantMessage)loaded[0];
        var toolCall = (ToolCall)assistant.Content[0];

        await Assert.That(toolCall.Arguments["command"]!.GetValue<string>()).IsEqualTo("ls");
        await Assert.That(toolCall.Arguments["flags"]!.AsArray().Count).IsEqualTo(2);
    }

    [Test]
    public async Task AppendMessage_UnsupportedMessageType_Throws()
    {
        var session = CodingSession.Create(_cwd, "m");
        var unsupported = new BashExecutionMessage { Command = "ls" };

        await Assert.That(() => session.AppendMessage(unsupported))
            .Throws<NotSupportedException>();
    }

    private static SessionRecord NewRecord(
        string id,
        string cwd = "/tmp/proj",
        string model = "test-model",
        string? title = null,
        long updatedAt = 0) =>
        new(id, cwd, model, title, CreatedAt: updatedAt, UpdatedAt: updatedAt);
}
