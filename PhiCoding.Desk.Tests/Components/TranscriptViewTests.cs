using PhiAgent;
using PhiCoding.Chat;
using PhiCoding.Desk.Components;
using PhiCoding.Desk.Tests.Helpers;

namespace PhiCoding.Desk.Tests.Components;

/// <summary>
/// <see cref="TranscriptView"/>: subscribes to the shared
/// <see cref="ChatTranscriptProjector"/> and DIFFs lines into a
/// <see cref="Aprillz.MewUI.Controls.StackPanel"/>. Structural assertions
/// (line count + stable Ids) avoid the MewUI render loop.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class TranscriptViewTests
{
    private static (MockSession session, ChatTranscriptProjector projector, TranscriptView view) Create()
    {
        var session = new MockSession();
        var projector = new ChatTranscriptProjector(session);
        var view = new TranscriptView();
        view.Bind(projector);
        return (session, projector, view);
    }

    [Test]
    public async Task Bind_EmptyProjection_NoLines()
    {
        var (_, _, view) = Create();

        await Assert.That(view.LineCount).IsEqualTo(0);
    }

    [Test]
    public async Task UserMessage_AddsOneLine()
    {
        var (session, _, view) = Create();

        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("hello"));

        await Assert.That(view.LineCount).IsEqualTo(1);
    }

    [Test]
    public async Task TextDeltas_StreamIntoSameLine()
    {
        var (session, _, view) = Create();

        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("hello "));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("world"));

        // Both deltas accumulate into one line; no extra line added.
        await Assert.That(view.LineCount).IsEqualTo(1);
        await Assert.That(view.LineIds.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ThinkingThenText_TwoLines()
    {
        var (session, _, view) = Create();

        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantThinkingStartEvent());
        session.EmitHarnessEvent(new AssistantThinkingDeltaEvent("reasoning"));
        session.EmitHarnessEvent(new AssistantThinkingEndEvent(new ThinkingBlock("reasoning")));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("answer"));

        await Assert.That(view.LineCount).IsEqualTo(2);
    }

    [Test]
    public async Task ToolCall_PendingThenComplete_OneStableLine()
    {
        var (session, _, view) = Create();

        var call = new ToolCall("call-1", "bash");
        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantToolCallEvent(call));
        var pendingCount = view.LineCount;
        var pendingId = view.LineIds.Single();

        session.EmitHarnessEvent(new ToolExecutionEndEvent(
            call, new ToolResult([new TextBlock("done")], null, IsError: false)));

        // The tool call completes in-place; line count stays 1 and the Id
        // is stable (the renderer DIFFs on Id, not position).
        await Assert.That(view.LineCount).IsEqualTo(pendingCount);
        await Assert.That(view.LineIds.Single()).IsEqualTo(pendingId);
    }

    [Test]
    public async Task SkillInvocation_AddsLine()
    {
        var (_, projector, view) = Create();

        var skillBlock = PhiCoding.Resources.SkillInvocation.Build(
            "my-skill", "/skills/my", "/skills/my", "body text", args: null);
        projector.SubmitUserLine(skillBlock);

        await Assert.That(view.LineCount).IsEqualTo(1);
    }

    [Test]
    public async Task PersistentError_AddsLine()
    {
        var (_, projector, view) = Create();

        projector.SubmitPersistentError("boom");

        await Assert.That(view.LineCount).IsEqualTo(1);
    }

    [Test]
    public async Task SubmitUserLine_PlainText_AddsOneLine()
    {
        var (_, projector, view) = Create();

        projector.SubmitUserLine("hello there");

        await Assert.That(view.LineCount).IsEqualTo(1);
    }

    [Test]
    public async Task ClearAndLoad_ReplacesProjection()
    {
        var (_, projector, view) = Create();
        projector.SubmitUserLine("pre-existing");
        await Assert.That(view.LineCount).IsEqualTo(1);

        projector.ClearAndLoad(
        [
            new UserMessage { Content = "from disk" },
            new AssistantMessage { Content = [new TextBlock("answer")], StopReason = StopReasons.Stop },
        ]);

        await Assert.That(view.LineCount).IsEqualTo(2);
    }
}