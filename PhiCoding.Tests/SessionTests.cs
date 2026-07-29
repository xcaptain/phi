using System.Text.Json.Nodes;
using PhiAgent;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

// SessionTests uses a per-test temp root and an exclusive parallel group
// so concurrent tests don't trample each other's index files.
[NotInParallel("session-tests")]
public class SessionTests : IDisposable
{
    private readonly string _root;

    public SessionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "phi-coding-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ──────────────────── SessionPaths / default id ────────────────────

    [Test]
    public async Task SessionPaths_EnsureRoot_CreatesDirectory()
    {
        SessionPaths.EnsureRoot(_root);

        await Assert.That(Directory.Exists(_root)).IsTrue();
    }

    [Test]
    public async Task DefaultSessionId_SameCwd_ReturnsSameId()
    {
        var id1 = CodingSession.DefaultSessionId("/tmp/foo");
        var id2 = CodingSession.DefaultSessionId("/tmp/foo");

        await Assert.That(id1).IsEqualTo(id2);
        await Assert.That(id1).StartsWith("default-");
        await Assert.That(id1.Length).IsEqualTo("default-".Length + 8);
    }

    [Test]
    public async Task DefaultSessionId_DifferentCwd_DifferentId()
    {
        var a = CodingSession.DefaultSessionId("/tmp/foo");
        var b = CodingSession.DefaultSessionId("/tmp/bar");

        await Assert.That(a).IsNotEqualTo(b);
    }

    // ──────────────────── SessionIndex ────────────────────

    [Test]
    public async Task SessionIndex_ListAll_EmptyWhenNoFile()
    {
        var index = new SessionIndex(SessionPaths.IndexFileIn(_root));

        await Assert.That(index.ListAll()).IsEmpty();
    }

    [Test]
    public async Task SessionIndex_Upsert_ThenList_ReturnsRecord()
    {
        var index = new SessionIndex(SessionPaths.IndexFileIn(_root));
        var record = NewRecord("id1", cwd: "/tmp/a");

        index.Upsert(record);

        var all = index.ListAll();
        await Assert.That(all.Count).IsEqualTo(1);
        await Assert.That(all[0].Id).IsEqualTo("id1");
    }

    [Test]
    public async Task SessionIndex_UpsertSameId_ReplacesNotDuplicates()
    {
        var index = new SessionIndex(SessionPaths.IndexFileIn(_root));
        index.Upsert(NewRecord("id1", title: "old"));
        index.Upsert(NewRecord("id1", title: "new"));

        var all = index.ListAll();
        await Assert.That(all.Count).IsEqualTo(1);
        await Assert.That(all[0].Title).IsEqualTo("new");
    }

    [Test]
    public async Task SessionIndex_GetById_ReturnsMatchingRecord()
    {
        var index = new SessionIndex(SessionPaths.IndexFileIn(_root));
        index.Upsert(NewRecord("id1", title: "first"));
        index.Upsert(NewRecord("id2", title: "second"));

        await Assert.That(index.Get("id2")?.Title).IsEqualTo("second");
        await Assert.That(index.Get("missing")).IsNull();
    }

    [Test]
    public async Task SessionIndex_ListForCwd_FiltersByPath()
    {
        var index = new SessionIndex(SessionPaths.IndexFileIn(_root));
        index.Upsert(NewRecord("a", cwd: "/tmp/proj-a"));
        index.Upsert(NewRecord("b", cwd: "/tmp/proj-b"));
        index.Upsert(NewRecord("c", cwd: "/tmp/proj-a"));

        var forA = index.ListForCwd("/tmp/proj-a");

        await Assert.That(forA.Select(r => r.Id).OrderBy(id => id))
            .IsEquivalentTo(["a", "c"]);
    }

    [Test]
    public async Task SessionIndex_ListAll_OrdersByUpdatedAtDescending()
    {
        var index = new SessionIndex(SessionPaths.IndexFileIn(_root));
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
        var session = CodingSession.GetOrCreateDefault(_root, "test-model", _root);

        await Assert.That(session.Id).IsEqualTo(CodingSession.DefaultSessionId(_root));
        await Assert.That(session.Cwd).IsEqualTo(_root);
        await Assert.That(session.Model).IsEqualTo("test-model");
        await Assert.That(session.LoadMessages()).IsEmpty();
    }

    [Test]
    public async Task GetOrCreateDefault_SecondCall_ReturnsSameSession()
    {
        var first = CodingSession.GetOrCreateDefault(_root, "test-model", _root);
        var second = CodingSession.GetOrCreateDefault(_root, "test-model", _root);

        await Assert.That(second.Id).IsEqualTo(first.Id);
    }

    [Test]
    public async Task Create_GeneratesUniqueIds()
    {
        var a = CodingSession.Create(_root, "m", _root);
        var b = CodingSession.Create(_root, "m", _root);

        await Assert.That(a.Id).IsNotEqualTo(b.Id);
    }

    [Test]
    public async Task AppendMessage_PersistsAndLoadsBack()
    {
        var session = CodingSession.Create(_root, "m", _root);

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

        // Re-open from disk to prove it really round-tripped.
        var reopened = CodingSession.Resume(session.Id, _root);
        var loaded = reopened.LoadMessages();

        await Assert.That(loaded.Count).IsEqualTo(3);
        await Assert.That(loaded[0]).IsTypeOf<UserMessage>();
        await Assert.That(((UserMessage)loaded[0]).Text).IsEqualTo("hello");
        await Assert.That(loaded[1]).IsTypeOf<AssistantMessage>();
        await Assert.That(((TextBlock)((AssistantMessage)loaded[1]).Content[0]).Text)
            .IsEqualTo("hi back");
        await Assert.That(loaded[2]).IsTypeOf<ToolResultMessage>();
        var tr = (ToolResultMessage)loaded[2];
        await Assert.That(tr.ToolCallId).IsEqualTo("c1");
        await Assert.That(tr.ToolName).IsEqualTo("bash");
        await Assert.That(tr.IsError).IsFalse();
    }

    [Test]
    public async Task Resume_NonExistentSession_Throws()
    {
        await Assert.That(() => CodingSession.Resume("nope", _root))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AppendMessage_UpdatesIndexTimestamp()
    {
        var session = CodingSession.Create(_root, "m", _root);
        var initialUpdatedAt = session.Record.UpdatedAt;

        await Task.Delay(5);

        session.AppendMessage(new UserMessage { Content = "x" });

        await Assert.That(session.Record.UpdatedAt).IsGreaterThan(initialUpdatedAt);
    }

    [Test]
    public async Task Touch_BumpsUpdatedAtInIndex()
    {
        var session = CodingSession.Create(_root, "m", _root);
        var before = session.Record.UpdatedAt;

        await Task.Delay(5);
        session.Touch();

        await Assert.That(session.Record.UpdatedAt).IsGreaterThan(before);
        var fromIndex = new SessionIndex(SessionPaths.IndexFileIn(_root)).Get(session.Id);
        await Assert.That(fromIndex).IsNotNull();
        await Assert.That(fromIndex!.UpdatedAt).IsEqualTo(session.Record.UpdatedAt);
    }

    [Test]
    public async Task Rename_UpdatesTitleInIndex()
    {
        var session = CodingSession.Create(_root, "m", _root);

        session.Rename("My Cool Session");

        var fromIndex = new SessionIndex(SessionPaths.IndexFileIn(_root)).Get(session.Id);
        await Assert.That(fromIndex?.Title).IsEqualTo("My Cool Session");
        await Assert.That(session.Record.Title).IsEqualTo("My Cool Session");
    }

    [Test]
    public async Task AppendMessage_ToolCallArgsRoundTrip()
    {
        var session = CodingSession.Create(_root, "m", _root);
        var args = JsonNode.Parse("""{"command":"ls","flags":["-la","-h"]}""")!.AsObject();

        session.AppendMessage(new AssistantMessage
        {
            Content = [new ToolCall("c1", "bash") { Arguments = args }],
            StopReason = StopReasons.ToolUse,
        });

        var loaded = CodingSession.Resume(session.Id, _root).LoadMessages();
        var assistant = (AssistantMessage)loaded[0];
        var toolCall = (ToolCall)assistant.Content[0];

        await Assert.That(toolCall.Arguments["command"]!.GetValue<string>()).IsEqualTo("ls");
        await Assert.That(toolCall.Arguments["flags"]!.AsArray().Count).IsEqualTo(2);
    }

    [Test]
    public async Task AppendMessage_UnsupportedMessageType_Throws()
    {
        var session = CodingSession.Create(_root, "m", _root);
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
