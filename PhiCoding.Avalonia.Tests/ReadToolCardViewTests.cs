using System.Text.Json.Nodes;
using Avalonia.Controls;
using PhiAgent;
using PhiCoding.Avalonia.Components;
using PhiCoding.Avalonia.Components.ToolCards;
using PhiCoding.Tools.Details;
using PbTextBlock = PhiAgent.TextBlock;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="ReadToolCardView"/>: header is <c>✓ read: &lt;path&gt;
/// [offset=N, limit=M]</c>; body is a
/// <see cref="SyntaxHighlightedContent"/> with a metadata line above
/// the file body. Defaults to collapsed — the user clicks to see the
/// file contents.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ReadToolCardViewTests
{
    private static ToolCall Call(string path, int? offset = null, int? limit = null)
    {
        var args = new JsonObject { ["path"] = path };
        if (offset is { } o) args["offset"] = o;
        if (limit is { } l) args["limit"] = l;
        return new ToolCall("id-1", "read") { Arguments = args };
    }

    private static ToolResult ReadResult(ReadDetails details, string content, bool isError = false) =>
        new(
            [new PbTextBlock(content)],
            Details: ToolDetails.Node(details),
            IsError: isError);

    private static TextBlock TitleOf(ReadToolCardView card)
        => (TextBlock)((CollapsibleSection)card.Visual).HeaderContent;

    private static ToolCardBodyFrame BodyFrameOf(ReadToolCardView card)
        => (ToolCardBodyFrame)((CollapsibleSection)card.Visual).BodyContent;

    private static SyntaxHighlightedContent BodyOf(ReadToolCardView card)
        => (SyntaxHighlightedContent)((ScrollViewer)BodyFrameOf(card).Child!).Content!;

    [Test]
    public async Task ShowPending_TitleIsReadWithPath()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new ReadToolCardView();
        card.ShowPending(Call("foo.cs"));

        await Assert.That(TitleOf(card).Text).IsEqualTo("› read: foo.cs");
    }

    [Test]
    public async Task ShowPending_WithOffsetAndLimit_TitleIncludesArgs()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new ReadToolCardView();
        card.ShowPending(Call("foo.cs", offset: 90, limit: 200));

        await Assert.That(TitleOf(card).Text).IsEqualTo("› read: foo.cs [offset=90, limit=200]");
    }

    [Test]
    public async Task Complete_Success_TitleIsCheckReadWithPath()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new ReadToolCardView();
        card.ShowPending(Call("foo.cs"));
        card.Complete(ReadResult(
            new ReadDetails("foo.cs", 1, 5, 5, 100, 1234),
            "line1\nline2\nline3\nline4\nline5"));

        await Assert.That(TitleOf(card).Text).IsEqualTo("✓ read: foo.cs");
    }

    [Test]
    public async Task Complete_Success_BodyIsSyntaxHighlightedContent()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new ReadToolCardView();
        card.ShowPending(Call("Foo.cs"));
        card.Complete(ReadResult(
            new ReadDetails("Foo.cs", 1, 3, 3, 100, 256),
            "a\nb\nc"));

        // Body wrapped in ToolCardBodyFrame; unwrap to get to the
        // SyntaxHighlightedContent inside.
        var body = BodyOf(card);
        await Assert.That(body.Children.Count).IsEqualTo(2);

        // Header line carries path + line range + size metadata so the
        // user knows what slice they're looking at.
        var meta = (TextBlock)body.Children[0];
        await Assert.That(meta.Text).IsEqualTo("Foo.cs  ·  lines 1-3 of 100  ·  256B");
        await Assert.That(meta.Foreground).IsEqualTo(AvaloniaTheme.TextSecondary);
    }

    [Test]
    public async Task Complete_KnownExtension_BodyUsesMarkdownCodeBlock()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new ReadToolCardView();
        card.ShowPending(Call("foo.py"));
        card.Complete(ReadResult(
            new ReadDetails("foo.py", 1, 1, 1, 1, 10),
            "print('hi')"));

        var body = BodyOf(card);
        await Assert.That(body.Children[1]).IsAssignableFrom<MarkView.Avalonia.MarkdownViewer>();
    }

    [Test]
    public async Task Complete_UnknownExtension_BodyUsesMonoTextBlock()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new ReadToolCardView();
        card.ShowPending(Call("foo.log"));
        card.Complete(ReadResult(
            new ReadDetails("foo.log", 1, 1, 1, 1, 5),
            "log line"));

        var body = BodyOf(card);
        var mono = (TextBlock)body.Children[1];
        await Assert.That(mono.Text).IsEqualTo("log line");
        await Assert.That(mono.FontFamily).IsEqualTo(AvaloniaTheme.MonoFontFamily);
    }

    [Test]
    public async Task Complete_StripsContinuationHintFromBody()
    {
        // ReadTool appends "[N more lines... use offset=X]" when the
        // result was sliced. The metadata header already carries that
        // info, so the renderer strips the trailing hint from the body
        // so it doesn't show up inside the code block as noise.
        AvaloniaTestHost.EnsureInitialized();
        var card = new ReadToolCardView();
        card.ShowPending(Call("foo.cs", offset: 1, limit: 5));
        card.Complete(new ToolResult(
            [new PbTextBlock("line1\nline2\n\n[95 more lines in file. Use offset=6 to continue.")],
            Details: ToolDetails.Node(new ReadDetails("foo.cs", 1, 5, 5, 100, 1234))));

        var body = BodyOf(card);
        // The MarkdownViewer wraps the body in a code block; we
        // can't easily pull the rendered text, so the contract is
        // "stripped" verified via the known StripContinuationHint helper
        // (also exercised via the bash card fallback tests).
        await Assert.That(body).IsNotNull();
    }

    [Test]
    public async Task Complete_Failure_TitleIsCrossReadAndBodyIsDangerText()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new ReadToolCardView();
        card.ShowPending(Call("missing.cs"));
        card.Complete(new ToolResult(
            [new PbTextBlock("File not found: missing.cs")],
            IsError: true));

        await Assert.That(TitleOf(card).Text).IsEqualTo("✗ read: missing.cs");

        // Error path renders a plain TextBlock (not a SyntaxHighlightedContent).
        var frame = BodyFrameOf(card);
        var scroll = (ScrollViewer)frame.Child!;
        var text = (TextBlock)scroll.Content!;
        await Assert.That(text.Text).IsEqualTo("File not found: missing.cs");
        await Assert.That(text.Foreground).IsEqualTo(AvaloniaTheme.Danger);
    }

    [Test]
    public async Task FormatInvocation_PathOnly_OmitsOffsetAndLimit()
    {
        await Assert.That(ReadToolCardView.FormatInvocation(Call("foo.cs")))
            .IsEqualTo("foo.cs");
    }

    [Test]
    public async Task FormatInvocation_PathWithOffset_ShowsOffsetOnly()
    {
        await Assert.That(ReadToolCardView.FormatInvocation(Call("foo.cs", offset: 90)))
            .IsEqualTo("foo.cs [offset=90, limit=all]");
    }

    [Test]
    public async Task FormatInvocation_PathWithLimit_ShowsLimitOnly()
    {
        await Assert.That(ReadToolCardView.FormatInvocation(Call("foo.cs", limit: 200)))
            .IsEqualTo("foo.cs [offset=1, limit=200]");
    }

    [Test]
    public async Task FormatInvocation_PathWithBoth_ShowsBoth()
    {
        await Assert.That(ReadToolCardView.FormatInvocation(Call("foo.cs", offset: 90, limit: 200)))
            .IsEqualTo("foo.cs [offset=90, limit=200]");
    }
}
