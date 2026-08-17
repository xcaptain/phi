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
/// <see cref="BashToolCardView"/>: header is <c>✓ Bash: &lt;command&gt;</c>
/// (long commands are truncated with <c>…</c>); body is a
/// <see cref="BashOutputView"/> (stdout + stderr mono blocks only) wrapped
/// in a <see cref="ToolCardBodyFrame"/> (scrollable, MaxHeight=400).
/// When <see cref="BashDetails"/> is missing the body falls back to
/// parsing <see cref="ToolResult.Content"/> textblocks.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class BashToolCardViewTests
{
    private static ToolCall Call(string command)
        => new("id-1", "bash") { Arguments = new JsonObject { ["command"] = command } };

    private static ToolResult BashResult(BashDetails details, bool isError = false) =>
        new(
            [
                new PbTextBlock(details.Stdout),
                new PbTextBlock(details.Stderr),
            ],
            Details: ToolDetails.Node(details),
            IsError: isError);

    /// <summary>Legacy-session shape: Details null, only the textual
    /// Content blocks are persisted.</summary>
    private static ToolResult LegacyBashResult(string stdout, string stderr, bool isError = false) =>
        new(
            [
                new PbTextBlock(stdout),
                new PbTextBlock(stderr),
            ],
            Details: null,
            IsError: isError);

    private static TextBlock TitleOf(BashToolCardView card)
        => (TextBlock)((CollapsibleSection)card.Visual).HeaderContent;

    /// <summary>The bash card's body is wrapped in a
    /// <see cref="ToolCardBodyFrame"/>; unwrap the Border → ScrollViewer
    /// → BashOutputView chain to get to the inner view.</summary>
    private static BashOutputView BodyOf(BashToolCardView card)
    {
        var frame = (ToolCardBodyFrame)((CollapsibleSection)card.Visual).BodyContent;
        var scroll = (ScrollViewer)frame.Child!;
        return (BashOutputView)scroll.Content!;
    }

    [Test]
    public async Task ShowPending_TitleIsBashWithCommand()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls -la"));

        await Assert.That(TitleOf(card).Text).IsEqualTo("› Bash: ls -la");
    }

    [Test]
    public async Task Complete_Success_TitleIsCheckBashWithCommand()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls"));
        card.Complete(BashResult(new BashDetails("ls", 0, 42, "ok", "")));

        await Assert.That(TitleOf(card).Text).IsEqualTo("✓ Bash: ls");
    }

    [Test]
    public async Task Complete_Failure_TitleIsCrossBashWithCommand()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("false"));
        card.Complete(BashResult(
            new BashDetails("false", 1, 123, "", "command not found"),
            isError: true));

        await Assert.That(TitleOf(card).Text).IsEqualTo("✗ Bash: false");
    }

    [Test]
    public async Task Complete_LongCommand_TruncatesWithEllipsisInHeader()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        var longCmd = new string('x', 200);
        card.ShowPending(Call(longCmd));
        card.Complete(BashResult(new BashDetails(longCmd, 0, 1, "", "")));

        // Header collapses long commands to <=80 chars + "…".
        var title = TitleOf(card).Text!;
        await Assert.That(title.Length).IsLessThanOrEqualTo("✓ Bash: ".Length + 80);
        await Assert.That(title).EndsWith("…");
    }

    [Test]
    public async Task Complete_StdoutOnly_BodyIsBashOutputViewWithStdout()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls"));
        card.Complete(BashResult(new BashDetails("ls", 0, 1, "file1\nfile2", "")));

        // Body is now BashOutputView wrapped in ToolCardBodyFrame; the
        // frame's Child is the BashOutputView's StackPanel.
        var body = BodyOf(card);
        await Assert.That(body.Children.Count).IsEqualTo(1);
        var stdoutBlock = (TextBlock)body.Children[0];
        await Assert.That(stdoutBlock.Text).IsEqualTo("file1\nfile2");
        await Assert.That(stdoutBlock.FontFamily).IsNotNull();
        await Assert.That(stdoutBlock.Foreground).IsEqualTo(AvaloniaTheme.TextPrimary);
    }

    [Test]
    public async Task Complete_StderrOnly_BodyStderrUsesDangerColor()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("false"));
        card.Complete(BashResult(
            new BashDetails("false", 1, 1, "", "command not found"),
            isError: true));

        var body = BodyOf(card);
        var stderrBlock = (TextBlock)body.Children[0];
        await Assert.That(stderrBlock.Text).IsEqualTo("command not found");
        await Assert.That(stderrBlock.Foreground).IsEqualTo(AvaloniaTheme.Danger);
    }

    [Test]
    public async Task Complete_BothStdoutAndStderr_BodyStacksBothSections()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("mixed"));
        card.Complete(BashResult(new BashDetails("mixed", 1, 1, "partial", "warn: deprecated")));

        var body = BodyOf(card);
        await Assert.That(body.Children.Count).IsEqualTo(2);
        var stdout = (TextBlock)body.Children[0];
        var stderr = (TextBlock)body.Children[1];
        await Assert.That(stdout.Text).IsEqualTo("partial");
        await Assert.That(stdout.Foreground).IsEqualTo(AvaloniaTheme.TextPrimary);
        await Assert.That(stderr.Text).IsEqualTo("warn: deprecated");
        await Assert.That(stderr.Foreground).IsEqualTo(AvaloniaTheme.Danger);
    }

    [Test]
    public async Task Complete_NoOutput_BodyShowsNoOutputHint()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("true"));
        card.Complete(BashResult(new BashDetails("true", 0, 1, "", "")));

        var body = BodyOf(card);
        var hint = (TextBlock)body.Children[0];
        await Assert.That(hint.Text).IsEqualTo("(no output)");
        await Assert.That(hint.Foreground).IsEqualTo(AvaloniaTheme.TextSecondary);
    }

    [Test]
    public async Task Complete_NoDetails_LegacyFallback_ShowsStdoutFromContent()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls"));
        card.Complete(LegacyBashResult(stdout: "file1\nfile2", stderr: ""));

        var body = BodyOf(card);
        var stdoutBlock = (TextBlock)body.Children[0];
        await Assert.That(stdoutBlock.Text).IsEqualTo("file1\nfile2");
    }

    [Test]
    public async Task Complete_Body_IsWrappedInToolCardBodyFrame()
    {
        // The body must live inside a ToolCardBodyFrame so long bash
        // output scrolls inside the transcript instead of stretching it.
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls"));
        card.Complete(BashResult(new BashDetails("ls", 0, 1, "ok", "")));

        var frame = (ToolCardBodyFrame)((CollapsibleSection)card.Visual).BodyContent;
        await Assert.That(frame.MaxHeight).IsEqualTo(ToolCardBodyFrame.DefaultMaxHeight);
        await Assert.That(frame.Child).IsAssignableFrom<ScrollViewer>();
    }
}
