using PhiAgent;
using PhiCoding.Tui;
using PhiCoding.Tests.Helpers;
using TextBlock = PhiAgent.TextBlock;
using DocumentFlow = XenoAtom.Terminal.UI.Controls.DocumentFlow;

namespace PhiCoding.Tests;

public class TuiDataTests
{
    [Test]
    public async Task ChatTranscript_AddPersistentError_AppendsItem()
    {
        var t = new ChatTranscript();
        await Task.CompletedTask;
        t.AddPersistentError("something broke");

        var flow = t.Visual as DocumentFlow;
        await Assert.That(flow).IsNotNull();
        await Assert.That(flow!.Items.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ChatTranscript_ClearAndLoad_RendersAllMessages()
    {
        var t = new ChatTranscript();
        await Task.CompletedTask;

        var msgs = new IAgentMessage[]
        {
            new UserMessage { Content = "hello" },
            new AssistantMessage { Content = [new TextBlock("world")], StopReason = StopReasons.Stop },
        };
        t.ClearAndLoad(msgs);

        var flow = t.Visual as DocumentFlow;
        await Assert.That(flow).IsNotNull();
        await Assert.That(flow!.Items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task MockSession_SubmitPrompt_RecordsCall()
    {
        var session = new MockSession();
        string? captured = null;
        session.OnSubmitPrompt = t => captured = t;

        session.SubmitPrompt("hello");

        await Assert.That(captured).IsEqualTo("hello");
        await Assert.That(session.LastSubmittedText).IsEqualTo("hello");
    }

    [Test]
    public async Task MockSession_Cancel_SetsFlag()
    {
        var session = new MockSession();

        session.Cancel();

        await Assert.That(session.CancelCalled).IsTrue();
    }

    [Test]
    public async Task MockSession_SetMessages_FiresStateChanged()
    {
        var session = new MockSession();
        SessionState? captured = null;
        session.StateChanged += s => captured = s;

        session.SetMessages(new UserMessage { Content = "hi" });

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Messages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MockSession_EmitHarnessEvent_FiresEvent()
    {
        var session = new MockSession();
        HarnessEvent? captured = null;
        session.HarnessEvent += e => captured = e;

        session.EmitHarnessEvent(new AssistantTextDeltaEvent("hi"));

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured).IsTypeOf<AssistantTextDeltaEvent>();
        await Assert.That(((AssistantTextDeltaEvent)captured!).Delta).IsEqualTo("hi");
    }

    [Test]
    public async Task ISession_CodingSession_ImplementsInterface()
    {
        var c = typeof(CodingSession).GetInterfaces();
        await Assert.That(c.Any(i => i == typeof(ISession))).IsTrue();
    }

    [Test]
    public async Task FullTurn_UserAndAssistant_OnlyTwoItems()
    {
        // Simulate a complete turn: user submits a prompt → session streams
        // an assistant response → turn ends. The transcript must contain
        // exactly 2 visual items (user message + assistant message), no
        // duplicates.

        var transcript = new ChatTranscript();
        var session = new MockSession();
        transcript.Bind(session);

        // 1. User submits message (the app does this before SubmitPrompt).
        transcript.AddUserMessage("hello");

        // 2. Turn starts.
        session.EmitHarnessEvent(new TurnStartEvent(1));

        // 3. Assistant streams text.
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("world"));

        // 4. Turn ends → streaming stops.
        session.EmitHarnessEvent(new TurnEndEvent(
            new AssistantMessage
            {
                Content = [new TextBlock("world")],
                StopReason = StopReasons.Stop,
            }));

        // 5. Session state catches up (StateChanged fires after the turn).
        session.SetMessages(
            new UserMessage { Content = "hello" },
            new AssistantMessage
            {
                Content = [new TextBlock("world")],
                StopReason = StopReasons.Stop,
            });

        // Assert: only the two items rendered via AddUserMessage + streaming.
        var flow = transcript.Visual as DocumentFlow;
        await Assert.That(flow).IsNotNull();
        await Assert.That(flow!.Items.Count).IsEqualTo(2);
        await Assert.That(session.State.Messages.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FullTurn_StateChangedAfterTurn_NoDuplicateRendering()
    {
        // Verify that calling SetMessages after the turn completes does NOT
        // add extra items to the transcript (regression guard for the
        // double-render bug).
        var session = new MockSession();

        // Bind transcripts: both share the same session.
        var t1 = new ChatTranscript();
        t1.Bind(session);

        // Simulate a full turn with AddUserMessage first.
        t1.AddUserMessage("hello");
        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("world"));

        // Remember item count before StateChanged fires.
        session.EmitHarnessEvent(new TurnEndEvent(
            new AssistantMessage { Content = [new TextBlock("world")], StopReason = StopReasons.Stop }));

        var countBeforeState = ((DocumentFlow)t1.Visual!).Items.Count;

        // StateChanged fires after turn.
        session.SetMessages(
            new UserMessage { Content = "hello" },
            new AssistantMessage { Content = [new TextBlock("world")], StopReason = StopReasons.Stop });

        var countAfterState = ((DocumentFlow)t1.Visual!).Items.Count;

        await Assert.That(countBeforeState).IsEqualTo(2);
        await Assert.That(countAfterState).IsEqualTo(2); // must NOT grow
    }
}
