using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MarkView.Avalonia;
using PhiAgent;
using PhiCoding.Avalonia.Components;
using PhiCoding.Avalonia.Tests.Helpers;
using PhiCoding.Chat;
using TextBlock = global::Avalonia.Controls.TextBlock;

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

        var text = (SelectableTextBlock)bubble.Child!;
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

        // view.Root is now the TranscriptLayout UserControl; walk into its
        // Content (ScrollViewer) to read the padding.
        var scroll = (global::Avalonia.Controls.ScrollViewer)((global::Avalonia.Controls.ContentControl)view.Root).Content!;
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

        // The line visual is a MarkdownViewer wrapped in a Border;
        // assert the projector's projection (the renderer input) carries the
        // accumulated text — that's the contract the view binds to.
        await Assert.That(projector.Current.OfType<AssistantTextLine>().Single().Text).IsEqualTo("Hello world");
    }

    [Test]
    public async Task AssistantLine_NoCardWrapper_DirectMarkdown()
    {
        // Assistant text is no longer wrapped in a Border card: the line
        // visual is the MarkdownViewer itself so it sits flush in
        // the document flow (matching the user bubble's "no card" feel).
        var (session, projector, view) = Create();

        session.EmitHarnessEvent(new AssistantTextDeltaEvent("hello"));

        var line = view.LineAt(0);
        await Assert.That(line).IsTypeOf<MarkdownViewer>();
    }

    [Test]
    public async Task ThinkingLine_IsCollapsibleSection_DefaultCollapsed()
    {
        // Thinking renders as a CollapsibleSection with a TextBlock title
        // and a TextBlock body. Defaults to collapsed so a long chain of
        // thought doesn't dominate the transcript; user expands on demand.
        var (session, projector, view) = Create();

        session.EmitHarnessEvent(new AssistantThinkingStartEvent());
        session.EmitHarnessEvent(new AssistantThinkingDeltaEvent("let me reason"));
        session.EmitHarnessEvent(new AssistantThinkingEndEvent(
            new ThinkingBlock("let me reason") { DurationMs = 3000 }));

        var section = (CollapsibleSection)view.LineAt(0);
        await Assert.That(section.IsExpanded).IsFalse();
        await Assert.That(section.HeaderContent).IsTypeOf<TextBlock>();
        await Assert.That(section.BodyContent).IsTypeOf<TextBlock>();
    }

    [Test]
    public async Task ToolCallLine_IsCollapsibleSection_DefaultCollapsed()
    {
        // Every tool card now goes through CollapsibleSection (collapsed by
        // default). The header title updates from pending → "✓ Bash …" when
        // the tool result arrives.
        var (session, projector, view) = Create();

        var call = new ToolCall("call-1", "bash")
        {
            Arguments = new System.Text.Json.Nodes.JsonObject { ["command"] = "ls" },
        };
        session.EmitHarnessEvent(new AssistantToolCallEvent(call));

        var section = (CollapsibleSection)view.LineAt(0);
        await Assert.That(section.IsExpanded).IsFalse();
        await Assert.That(section.HeaderContent).IsTypeOf<TextBlock>();
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
    public async Task Resume_ToolResultWithBashDetails_RendersStdoutInBody()
    {
        // Resume edge: a ToolResultMessage carrying BashDetails in
        // `details` must produce a fully-rendered bash card — title with
        // exit/duration and body with the actual stdout — not the
        // pending-placeholder "…" or the textual-only fallback
        // "<no output>". Regression: pre-fix, CreateToolCallHandle hard-
        // coded the line's ResultState to Pending and never called
        // Complete, so the card stayed in the placeholder state and the
        // persisted Details (BashDetails) was never read by the card.
        AvaloniaTestHost.EnsureInitialized();
        var (session, projector, view) = Create();

        var bashDetails = PhiCoding.Tools.Details.ToolDetails.Node(
            new PhiCoding.Tools.Details.BashDetails(
                Command: "ls",
                ExitCode: 0,
                DurationMs: 42,
                Stdout: "file1\nfile2",
                Stderr: ""));

        var messages = new List<PhiAgent.IAgentMessage>
        {
            new PhiAgent.UserMessage { Content = "list files" },
            new PhiAgent.AssistantMessage
            {
                Content = [new PhiAgent.ToolCall("call-1", "bash")
                {
                    Arguments = new System.Text.Json.Nodes.JsonObject { ["command"] = "ls" },
                }],
                StopReason = PhiAgent.StopReasons.ToolUse,
            },
            new PhiAgent.ToolResultMessage
            {
                ToolCallId = "call-1",
                ToolName = "bash",
                Content = [new PhiAgent.TextBlock("file1\nfile2")],
                IsError = false,
                Details = bashDetails,
            },
        };

        projector.ClearAndLoad(messages);

        // Two lines: UserTextLine, ToolCallLine (Completed).
        await Assert.That(view.LineCount).IsEqualTo(2);

        var section = (PhiCoding.Avalonia.Components.CollapsibleSection)view.LineAt(1);
        // Body is wrapped in ToolCardBodyFrame → ScrollViewer → BashOutputView.
        var frame = (PhiCoding.Avalonia.Components.ToolCards.ToolCardBodyFrame)section.BodyContent;
        var scroll = (global::Avalonia.Controls.ScrollViewer)frame.Child!;
        var body = (PhiCoding.Avalonia.Components.ToolCards.BashOutputView)scroll.Content!;
        var stdoutBlock = (global::Avalonia.Controls.TextBlock)body.Children[^1];
        await Assert.That(stdoutBlock.Text).IsEqualTo("file1\nfile2");
    }

    [Test]
    public async Task Resume_ToolResultWithoutDetails_LegacyFallback_ShowsStdoutFromContent()
    {
        // A legacy ToolResultMessage (no `details` payload, e.g. persisted
        // before Details started being round-tripped) must still render
        // the actual bash output. The BashOutputView's stdout falls back
        // to reading the first Content textblock when BashDetails is null.
        AvaloniaTestHost.EnsureInitialized();
        var (session, projector, view) = Create();

        var messages = new List<PhiAgent.IAgentMessage>
        {
            new PhiAgent.UserMessage { Content = "run it" },
            new PhiAgent.AssistantMessage
            {
                Content = [new PhiAgent.ToolCall("call-1", "bash")
                {
                        Arguments = new System.Text.Json.Nodes.JsonObject { ["command"] = "ls" },
                    }],
                StopReason = PhiAgent.StopReasons.ToolUse,
            },
            new PhiAgent.ToolResultMessage
            {
                ToolCallId = "call-1",
                ToolName = "bash",
                Content = [new PhiAgent.TextBlock("ok")],
                IsError = false,
                // Details intentionally null — legacy entry shape.
            },
        };

        projector.ClearAndLoad(messages);

        await Assert.That(view.LineCount).IsEqualTo(2);

        var section = (PhiCoding.Avalonia.Components.CollapsibleSection)view.LineAt(1);
        var frame = (PhiCoding.Avalonia.Components.ToolCards.ToolCardBodyFrame)section.BodyContent;
        var scroll = (global::Avalonia.Controls.ScrollViewer)frame.Child!;
        var body = (PhiCoding.Avalonia.Components.ToolCards.BashOutputView)scroll.Content!;
        // Last child is the stdout mono TextBlock; legacy fallback reads
        // from Content so the user sees "ok" rather than "(no output)".
        var textBlock = (global::Avalonia.Controls.TextBlock)body.Children[^1];
        await Assert.That(textBlock.Text).IsEqualTo("ok");
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
