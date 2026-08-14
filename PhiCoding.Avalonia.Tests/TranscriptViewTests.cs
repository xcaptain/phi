using Avalonia.Controls;
using PhiAgent;
using PhiCoding.Avalonia.Components;
using PhiCoding.Avalonia.Tests.Helpers;
using PhiCoding.Chat;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="TranscriptView"/>: DIFFs the projector's <see cref="ChatLine"/>s
/// into the panel by stable Id, and patches streaming lines in place.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class TranscriptViewTests
{
    private static (MockSession session, ChatTranscriptProjector projector, TranscriptView view) Create()
    {
        AvaloniaTestHost.EnsureInitialized();
        var session = new MockSession();
        var projector = new ChatTranscriptProjector(session);
        var view = new TranscriptView(dispatchToUi: a => a());
        view.Bind(projector);
        return (session, projector, view);
    }

    [Test]
    public async Task UserLine_AddsUserBubble()
    {
        var (_, projector, view) = Create();

        projector.SubmitUserLine("hello there");

        await Assert.That(view.LineCount).IsEqualTo(1);
        await Assert.That(view.LineIds.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Root_HasDocumentStyleSidePadding()
    {
        // The transcript must not run lines to the window edge: generous,
        // symmetric horizontal padding on the scroll container (document
        // reading margins) plus vertical breathing room.
        var (_, _, view) = Create();

        var scroll = (global::Avalonia.Controls.ScrollViewer)view.Root;
        await Assert.That(scroll.Padding.Left).IsEqualTo(48);
        await Assert.That(scroll.Padding.Right).IsEqualTo(48);
        await Assert.That(scroll.Padding.Top).IsGreaterThan(0);
        await Assert.That(scroll.Padding.Bottom).IsGreaterThan(0);
    }

    [Test]
    public async Task StreamingAssistantText_AddsAndPatchesLine()
    {
        var (session, projector, view) = Create();

        session.EmitHarnessEvent(new AssistantTextDeltaEvent("Hello "));
        session.EmitHarnessEvent(new AssistantTextDeltaEvent("world"));

        await Assert.That(view.LineCount).IsEqualTo(1);

        // The line visual is a MarkdownScrollViewer wrapped in a Border;
        // assert the projector's projection (the renderer input) carries the
        // accumulated text — that's the contract the view binds to.
        await Assert.That(projector.Current.OfType<AssistantTextLine>().Single().Text).IsEqualTo("Hello world");
    }

    [Test]
    public async Task MultipleLines_KeepStableIds()
    {
        var (_, projector, view) = Create();

        projector.SubmitUserLine("first");
        var firstIds = view.LineIds.ToList();
        projector.SubmitUserLine("second");

        await Assert.That(view.LineCount).IsEqualTo(2);
        await Assert.That(view.LineIds).Contains(firstIds[0]);
    }

    [Test]
    public async Task PersistentError_RendersErrorBubble()
    {
        var (_, projector, view) = Create();

        projector.SubmitPersistentError("boom");

        await Assert.That(view.LineCount).IsEqualTo(1);
    }

    [Test]
    public async Task ToolCallLine_CreatesCard()
    {
        var (session, projector, view) = Create();

        var call = new ToolCall("call-1", "bash")
        {
            Arguments = new System.Text.Json.Nodes.JsonObject { ["command"] = "ls" },
        };
        session.EmitHarnessEvent(new AssistantToolCallEvent(call));

        await Assert.That(view.LineCount).IsEqualTo(1);
        await Assert.That(projector.Current.OfType<ToolCallLine>().Single().ToolName).IsEqualTo("bash");
    }

    [Test]
    public async Task ClearAndLoad_RepopulatesFromMessages()
    {
        var (_, projector, view) = Create();
        projector.SubmitUserLine("stale");
        await Assert.That(view.LineCount).IsEqualTo(1);

        projector.ClearAndLoad([
            new PhiAgent.UserMessage { Content = "fresh" },
        ]);

        await Assert.That(view.LineCount).IsEqualTo(1);
        await Assert.That(projector.Current.OfType<UserTextLine>().Single().Text).IsEqualTo("fresh");
    }
}
