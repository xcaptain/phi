using Phi.Agent;
using Phi.Chat;
using Phi.Resources;
using Phi.Tests.Helpers;

namespace Phi.Tests.Chat;

/// <summary>
/// <see cref="ChatTranscriptProjector"/>: subscribe to <see cref="ISession"/>
/// events, project them into a stable, ordered list of <see cref="ChatLine"/>s
/// with stable Ids. Renderers (TUI, future Desk) DIFF the projection against
/// their own visual tree.
/// </summary>
[NotInParallel(ChatTestGroups.Projector)]
public class ChatTranscriptProjectorTests
{
    [Test]
    public async Task Constructor_OnEmptySession_ProducesEmptyProjection()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        await Assert.That(projector.Current).IsEmpty();
    }

    [Test]
    public async Task Constructor_WithPriorMessages_RendersAll()
    {
        var session = new MockSession();
        session.SetMessages(
            new UserMessage { Content = "hi" },
            new AssistantMessage
            {
                Content = [new TextBlock("hello")],
                StopReason = StopReasons.Stop,
            });
        using var projector = new ChatTranscriptProjector(session);

        await Assert.That(projector.Current.Count).IsEqualTo(2);
        await Assert.That(projector.Current[0]).IsTypeOf<UserTextLine>();
        await Assert.That(projector.Current[1]).IsTypeOf<AssistantTextLine>();
    }

    [Test]
    public async Task Constructor_ReplayedMessages_HaveStableLineIds()
    {
        var session = new MockSession();
        session.SetMessages(new UserMessage { Content = "x" });
        using var projector = new ChatTranscriptProjector(session);

        var first = projector.Current[0];
        await Assert.That(first.Id).IsEqualTo("u0");
    }

    [Test]
    public async Task SubmitUserLine_AppendsUserTextLineAndNotifies()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);
        var fired = 0;
        projector.Changed += _ => fired++;

        projector.SubmitUserLine("hello");

        await Assert.That(fired).IsEqualTo(1);
        var user = (UserTextLine)projector.Current[^1];
        await Assert.That(user.Text).IsEqualTo("hello");
        await Assert.That(user.Id).IsEqualTo("u0");
    }

    [Test]
    public async Task SubmitUserLine_SkillBlock_AppendsSkillInvocationLine()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        var block = SkillInvocation.Build("my-skill", "/skills/my", "/skills/my", "body text", args: null);
        projector.SubmitUserLine(block);

        var line = (SkillInvocationLine)projector.Current[^1];
        await Assert.That(line.SkillName).IsEqualTo("my-skill");
        await Assert.That(line.Body).Contains("body text");
    }

    [Test]
    public async Task SubmitUserLine_CompactionSummary_AppendsCompactionDivider()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        projector.SubmitUserLine(ContextWindow.CompactionSummaryPrefix + "compacted earlier");

        await Assert.That(projector.Current[^1]).IsTypeOf<CompactionDividerLine>();
        var divider = (CompactionDividerLine)projector.Current[^1];
        await Assert.That(divider.SummaryLine).IsEqualTo(ContextWindow.CompactionSummaryPrefix.TrimEnd('\n'));
    }

    [Test]
    public async Task TurnStart_ResetsRenderedCount_AndSetsStreaming()
    {
        var session = new MockSession();
        session.SetMessages(new UserMessage { Content = "prior" });
        using var projector = new ChatTranscriptProjector(session);
        await Assert.That(projector.Current.Count).IsEqualTo(1);

        session.EmitHarnessEvent(new TurnStartEvent(1));

        // After TurnStartEvent, _isStreaming=true so StateChanged replay is
        // suppressed; the existing line stays put, no new line is added yet.
        await Assert.That(projector.Current.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ThinkingStartDeltaEnd_AppendsThinkingLineWithDuration()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantThinkingStartEvent());
        session.EmitHarnessEvent(new AssistantThinkingDeltaEvent("step 1"));
        session.EmitHarnessEvent(new AssistantThinkingDeltaEvent(", step 2"));
        session.EmitHarnessEvent(new AssistantThinkingEndEvent(
            new ThinkingBlock("step 1, step 2") { DurationMs = 1500 }));

        await Assert.That(projector.Current.Count).IsEqualTo(1);
        var thinking = (ThinkingLine)projector.Current[^1];
        await Assert.That(thinking.Text).IsEqualTo("step 1, step 2");
        await Assert.That(thinking.IsStreaming).IsFalse();
        await Assert.That(thinking.Duration).IsEqualTo(TimeSpan.FromMilliseconds(1500));
    }

    [Test]
    public async Task ThinkingEnd_WithoutDurationMs_FallsBackToWallClock()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantThinkingStartEvent());
        Thread.Sleep(15);
        session.EmitHarnessEvent(new AssistantThinkingEndEvent(new ThinkingBlock("x")));

        var thinking = (ThinkingLine)projector.Current[^1];
        await Assert.That(thinking.Duration).IsNotNull();
        await Assert.That(thinking.Duration!.Value.TotalMilliseconds).IsGreaterThanOrEqualTo(10);
    }

    [Test]
    public async Task TextDeltas_DuringSameStream_AccumulateIntoSingleLine()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("hello "));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("world"));

        await Assert.That(projector.Current.Count).IsEqualTo(1);
        var text = (AssistantTextLine)projector.Current[^1];
        await Assert.That(text.Text).IsEqualTo("hello world");
        await Assert.That(text.IsStreaming).IsTrue();
    }

    [Test]
    public async Task ThinkingThenText_RendersTwoSeparateLines()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantThinkingStartEvent());
        session.EmitHarnessEvent(new AssistantThinkingDeltaEvent("thinking"));
        session.EmitHarnessEvent(new AssistantThinkingEndEvent(new ThinkingBlock("thinking")));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("answer"));

        await Assert.That(projector.Current.Count).IsEqualTo(2);
        await Assert.That(projector.Current[0]).IsTypeOf<ThinkingLine>();
        await Assert.That(projector.Current[1]).IsTypeOf<AssistantTextLine>();
    }

    [Test]
    public async Task AssistantToolCall_AddsPendingToolCallLine()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        session.EmitHarnessEvent(new TurnStartEvent(1));
        var call = new ToolCall("call-1", "read")
        {
            Arguments = new System.Text.Json.Nodes.JsonObject { ["path"] = "x.cs" },
        };
        session.EmitHarnessEvent(new AssistantToolCallEvent(call));

        var toolLine = (ToolCallLine)projector.Current[^1];
        await Assert.That(toolLine.ToolCallId).IsEqualTo("call-1");
        await Assert.That(toolLine.ToolName).IsEqualTo("read");
        await Assert.That(toolLine.ResultState).IsEqualTo(ToolResultState.Pending);
        await Assert.That(toolLine.Descriptor.Title).IsEqualTo("read");
    }

    [Test]
    public async Task ToolExecutionEnd_UpdatesToolCallLineToCompleted()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        var call = new ToolCall("call-1", "bash");
        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantToolCallEvent(call));
        session.EmitHarnessEvent(new ToolExecutionEndEvent(call, new ToolResult([new TextBlock("ok")], null, IsError: false)));

        var toolLine = (ToolCallLine)projector.Current[^1];
        await Assert.That(toolLine.ResultState).IsEqualTo(ToolResultState.Completed);
        await Assert.That(toolLine.ResultText).IsEqualTo("ok");
    }

    [Test]
    public async Task ToolExecutionEnd_WithError_MarksFailed()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        var call = new ToolCall("call-1", "bash");
        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantToolCallEvent(call));
        session.EmitHarnessEvent(new ToolExecutionEndEvent(call, new ToolResult([new TextBlock("bad")], null, IsError: true)));

        var toolLine = (ToolCallLine)projector.Current[^1];
        await Assert.That(toolLine.ResultState).IsEqualTo(ToolResultState.Failed);
    }

    [Test]
    public async Task ResetRenderedCount_DoesNotEraseExistingLines()
    {
        // ResetRenderedCount is a defensive call from PromptInput.LoadSkillAsync:
        // it must NOT clear the existing projection (which already includes
        // the skill card just added via SubmitUserLine). It only zeros the
        // replay cursor so a subsequent StateChanged would replay from 0.
        var session = new MockSession();
        session.SetMessages(new UserMessage { Content = "prior" });
        using var projector = new ChatTranscriptProjector(session);
        await Assert.That(projector.Current.Count).IsEqualTo(1);

        projector.ResetRenderedCount();
        await Assert.That(projector.Current.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SubmitPersistentError_AppendsPersistentErrorLine()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);
        var fired = 0;
        projector.Changed += _ => fired++;

        projector.SubmitPersistentError("boom");

        await Assert.That(fired).IsEqualTo(1);
        await Assert.That(projector.Current[^1]).IsTypeOf<PersistentErrorLine>();
    }

    [Test]
    public async Task ChangedEvent_FiresOncePerMutation()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);
        var fired = 0;
        projector.Changed += _ => fired++;

        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("a"));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("b"));
        session.EmitHarnessEvent(new TurnEndEvent(new AssistantMessage { StopReason = StopReasons.Stop }));

        // One per event (TurnStart, two text deltas, TurnEnd) = 4
        await Assert.That(fired).IsEqualTo(4);
    }

    [Test]
    public async Task SubmitCustomLine_AddsCustomLine_WithGivenFields()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);
        var fired = 0;
        projector.Changed += _ => fired++;

        projector.SubmitCustomLine("my-ext:progress", "line-1", "Building…",
            new Dictionary<string, object?> { ["percent"] = 42 });

        await Assert.That(fired).IsEqualTo(1);
        await Assert.That(projector.Current).Count().IsEqualTo(1);
        await Assert.That(projector.Current[0]).IsTypeOf<CustomLine>();
        var line = (CustomLine)projector.Current[0];
        await Assert.That(line.LineType).IsEqualTo("my-ext:progress");
        await Assert.That(line.Content).IsEqualTo("Building…");
        await Assert.That(line.Details!["percent"]).IsEqualTo(42);
        // Explicit id is preserved (renderers DIFF on it).
        await Assert.That(line.Id).IsEqualTo("line-1");
    }

    [Test]
    public async Task SubmitCustomLine_EmptyId_AssignsGeneratedId()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);

        projector.SubmitCustomLine("ext:thing", id: null, content: "body");

        await Assert.That(projector.Current[0]).IsTypeOf<CustomLine>();
        var line = (CustomLine)projector.Current[0];
        await Assert.That(line.Id).IsNotEmpty();
        await Assert.That(line.Id).StartsWith("cu");
    }

    [Test]
    public async Task ClearAndLoad_ReplacesProjectionEntirely()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session);
        projector.SubmitUserLine("pre-existing");

        projector.ClearAndLoad([new UserMessage { Content = "from disk" }]);

        await Assert.That(projector.Current.Count).IsEqualTo(1);
        var line = (UserTextLine)projector.Current[0];
        await Assert.That(line.Text).IsEqualTo("from disk");
    }

    [Test]
    public async Task ToolExecutionEnd_ForUnknownCallId_SynthesizesAndCompletes()
    {
        // Resume edge: the harness hands us a ToolResultMessage without
        // ever having emitted a streaming AssistantToolCallEvent (the call
        // was replayed from history). The projector should fabricate a
        // pending ToolCallLine and immediately complete it.
        var session = new MockSession();
        session.SetMessages(
            new AssistantMessage
            {
                Content = [new ToolCall("orphan", "edit")],
                StopReason = StopReasons.ToolUse,
            },
            new ToolResultMessage { ToolCallId = "orphan", ToolName = "edit" });
        using var projector = new ChatTranscriptProjector(session);

        var toolLine = projector.Current.OfType<ToolCallLine>().Single();
        await Assert.That(toolLine.ResultState).IsEqualTo(ToolResultState.Completed);
    }
}
