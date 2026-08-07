using PhiCoding.Chat;
using PhiCoding.Desk.Components;
using PhiCoding.Providers;
using PhiCoding.Desk.Tests.Helpers;

namespace PhiCoding.Desk.Tests.Components;

/// <summary>
/// <see cref="PromptInputView"/>: the input shell dispatches editor text
/// through the shared slash-command matcher and the session, and writes the
/// user's own message into the projector so it renders immediately. Tests
/// drive the internal text observable and assert the session action that
/// fired.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class PromptInputViewTests
{
    private static (MockSession session, FakeSessionNavigator navigator, PromptInputView view, ChatTranscriptProjector projector) Create()
    {
        var session = new MockSession();
        var navigator = new FakeSessionNavigator(session);
        var projector = new ChatTranscriptProjector(session);
        var view = new PromptInputView(session, navigator, new ProviderManager(), projector);
        view.Build();
        return (session, navigator, view, projector);
    }

    [Test]
    public async Task Submit_PlainText_CallsSessionSubmitPrompt()
    {
        var (session, _, view, _) = Create();

        view.Text.Value = "hello world";
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsEqualTo("hello world");
    }

    [Test]
    public async Task Submit_PlainText_AddsUserLineToProjector()
    {
        var (_, _, view, projector) = Create();

        view.Text.Value = "hello world";
        view.SubmitForTest();

        // The user's own message must appear in the transcript immediately
        // (the session only exposes new messages at TurnEndEvent).
        await Assert.That(projector.Current.OfType<UserTextLine>().Any(l => l.Text == "hello world")).IsTrue();
    }

    [Test]
    public async Task Submit_EmptyText_Ignored()
    {
        var (session, _, view, projector) = Create();

        view.Text.Value = "   ";
        view.SubmitForTest();

        await Assert.That(session.LastSubmittedText).IsNull();
        await Assert.That(projector.Current).IsEmpty();
    }

    [Test]
    public async Task Submit_SlashNew_NavigatesToNewSession()
    {
        var (_, navigator, view, _) = Create();

        view.Text.Value = "/new";
        view.SubmitForTest();

        await Assert.That(navigator.NavigateToNewCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Submit_WhileRunning_QueuesSteering()
    {
        var (session, _, view, projector) = Create();
        session.UpdateState(s => s with { IsRunning = true });

        view.Text.Value = "steer me";
        view.SubmitForTest();

        // While running, a plain prompt is queued, not submitted as a
        // direct SubmitPrompt call — but the user's message still renders.
        await Assert.That(session.LastSubmittedText).IsNull();
        await Assert.That(projector.Current.OfType<UserTextLine>().Any(l => l.Text == "steer me")).IsTrue();
    }
}