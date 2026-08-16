using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    public async Task UserBubble_IsRightAlignedAndPurple()
    {
        // The user bubble is a two-column Grid wrapper (the line element);
        // the second column holds a single Border sized to content and
        // right-aligned within the 4* column. Background is the theme
        // Accent (purple); text is AccentText (white); no border stroke.
        var (_, projector, view) = Create();

        projector.SubmitUserLine("hello there");

        var wrapper = (Grid)view.LineAt(0);
        await Assert.That(wrapper).IsTypeOf<Grid>();
        await Assert.That(wrapper.HorizontalAlignment).IsEqualTo(HorizontalAlignment.Stretch);

        var bubble = wrapper.Children.OfType<Border>().Single();
        await Assert.That(bubble.HorizontalAlignment).IsEqualTo(HorizontalAlignment.Right);
        await Assert.That(bubble.VerticalAlignment).IsEqualTo(VerticalAlignment.Top);
        await Assert.That(bubble.Background).IsEqualTo(PhiCoding.Avalonia.AvaloniaTheme.Accent);
        await Assert.That(bubble.BorderThickness).IsEqualTo(new Thickness(0));
        await Assert.That(bubble.CornerRadius).IsEqualTo(new CornerRadius(10));

        var text = (SelectableTextBlock)bubble.Child;
        await Assert.That(text.Text).IsEqualTo("hello there");
        await Assert.That(text.Foreground).IsEqualTo(PhiCoding.Avalonia.AvaloniaTheme.AccentText);
        await Assert.That(text.TextWrapping).IsEqualTo(TextWrapping.Wrap);
    }

    [Test]
    public async Task UserBubble_WrapperCapsWidthAt80Percent()
    {
        // The wrapper's two columns are "*" + "4*" so the bubble column is
        // exactly 80% of the panel width. That's the implicit max-width —
        // any text beyond 80% wraps inside the bubble instead of stretching
        // it to the panel edge.
        var (_, projector, view) = Create();

        projector.SubmitUserLine("hello there");

        var wrapper = (Grid)view.LineAt(0);
        await Assert.That(wrapper.ColumnDefinitions.Count).IsEqualTo(2);
        await Assert.That(wrapper.ColumnDefinitions[0].Width).IsEqualTo(new GridLength(1, GridUnitType.Star));
        await Assert.That(wrapper.ColumnDefinitions[1].Width).IsEqualTo(new GridLength(4, GridUnitType.Star));
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
