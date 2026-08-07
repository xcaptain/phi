using Aprillz.MewUI;
using PhiAgent;
using PhiCoding.Chat;
using PhiCoding.Desk.Components;
using PhiCoding.Desk.Tests.Helpers;

namespace PhiCoding.Desk.Tests.Components;

/// <summary>
/// <see cref="TranscriptView"/> dynamic rendering: lines appended to the
/// panel AFTER the initial layout (the real submit flow) must be arranged
/// with a non-zero height, not just counted in the panel.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class TranscriptViewLayoutTests
{
    private const double Width = 800;
    private const double Height = 600;

    private static void Layout(TranscriptView view)
    {
        view.Root.Measure(new Size(Width, Height));
        view.Root.Arrange(new Rect(0, 0, Width, Height));
    }

    [Test]
    public async Task UserLineAppendedAfterLayout_IsArrangedWithHeight()
    {
        MewTestHost.EnsureBackend();
        var session = new MockSession();
        var projector = new ChatTranscriptProjector(session);
        var view = new TranscriptView();
        view.Bind(projector);
        Layout(view);

        // Simulate the submit flow: the line arrives after first render.
        projector.SubmitUserLine("hello from the user");

        // Re-layout, then the line must occupy real space.
        Layout(view);
        await Assert.That(view.LineCount).IsEqualTo(1);
        await Assert.That(view.LineAt(0).RenderSize.Height).IsGreaterThan(0);
    }

    [Test]
    public async Task AssistantLineAppendedAfterLayout_IsArrangedWithHeight()
    {
        MewTestHost.EnsureBackend();
        var session = new MockSession();
        var projector = new ChatTranscriptProjector(session);
        var view = new TranscriptView();
        view.Bind(projector);
        Layout(view);

        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("model answer"));

        Layout(view);
        await Assert.That(view.LineCount).IsEqualTo(1);
        await Assert.That(view.LineAt(0).RenderSize.Height).IsGreaterThan(0);
    }

    [Test]
    public async Task MultiLineConversation_AllLinesArranged()
    {
        MewTestHost.EnsureBackend();
        var session = new MockSession();
        var projector = new ChatTranscriptProjector(session);
        var view = new TranscriptView();
        view.Bind(projector);

        projector.SubmitUserLine("first");
        session.EmitHarnessEvent(new TurnStartEvent(1));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("reply one"));
        session.EmitHarnessEvent(new TurnEndEvent(new AssistantMessage
        {
            Content = [new TextBlock("reply one")],
            StopReason = StopReasons.Stop,
        }));
        projector.SubmitUserLine("second");

        Layout(view);
        await Assert.That(view.LineCount).IsEqualTo(3);
        for (var i = 0; i < view.LineCount; i++)
        {
            await Assert.That(view.LineAt(i).RenderSize.Height).IsGreaterThan(0);
        }
    }
}
