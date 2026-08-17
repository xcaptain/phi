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
/// <see cref="BashToolCardView"/>: title shows <c>✓ Bash 169 ms</c> /
/// <c>✗ Bash 169 ms</c> (just name + duration badge, no command). Body
/// is a <see cref="BashOutputView"/>: command row on top (with copy
/// button), stdout / stderr split below, mono font throughout. When
/// <see cref="BashDetails"/> is missing (legacy sessions without persisted
/// Details), the body falls back to <see cref="ToolResult.Content"/>
/// textblocks via the BashTool emit convention
/// (<c>[TextBlock(stdout), TextBlock(stderr)]</c>) so the user still sees
/// the actual output.
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

    /// <summary>
    /// Legacy-session shape: <see cref="ToolResult.Details"/> is null,
    /// only the textual <see cref="ContentBlock"/>s are persisted. Used
    /// to verify the body falls back to reading stdout / stderr from
    /// Content rather than <c>&lt;no output&gt;</c>.
    /// </summary>
    private static ToolResult LegacyBashResult(string stdout, string stderr, bool isError = false) =>
        new(
            [
                new PbTextBlock(stdout),
                new PbTextBlock(stderr),
            ],
            Details: null,
            IsError: isError);

    /// <summary>Returns the header TextBlock of the card's collapsible section.</summary>
    private static TextBlock TitleOf(BashToolCardView card)
        => (TextBlock)((CollapsibleSection)card.Visual).HeaderContent;

    /// <summary>Returns the body Control of the card's collapsible section.</summary>
    private static BashOutputView BodyOf(BashToolCardView card)
        => (BashOutputView)((CollapsibleSection)card.Visual).BodyContent;

    [Test]
    public async Task ShowPending_TitleIsBash()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls -la"));

        await Assert.That(TitleOf(card).Text).IsEqualTo("Bash");
    }

    [Test]
    public async Task Complete_Success_TitleIncludesBashNameAndDuration()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls"));
        card.Complete(BashResult(new BashDetails("ls", 0, 42, "ok", "")));

        var title = TitleOf(card).Text;
        await Assert.That(title).Contains("✓");
        await Assert.That(title).Contains("Bash");
        await Assert.That(title).Contains("42ms");
        await Assert.That(title).DoesNotContain("ls"); // command moved to body
    }

    [Test]
    public async Task Complete_Failure_TitleShowsRedStatusAndDuration()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("false"));
        // BashTool maps non-zero ExitCode to IsError=true; mirror that here.
        card.Complete(BashResult(
            new BashDetails("false", 1, 123, "", "command not found"),
            isError: true));

        var title = TitleOf(card).Text;
        await Assert.That(title).Contains("✗");
        await Assert.That(title).Contains("Bash");
        await Assert.That(title).Contains("123ms");
    }

    [Test]
    public async Task Complete_StdoutOnly_BodyIsBashOutputViewWithStdout()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls"));
        card.Complete(BashResult(new BashDetails("ls", 0, 1, "file1\nfile2", "")));

        var body = BodyOf(card);
        await Assert.That(body.Children.Count).IsGreaterThanOrEqualTo(2);

        // First child: command row (Border wrapping DockPanel with copy button).
        var commandBorder = (Border)body.Children[0];
        var commandText = (TextBlock)((DockPanel)commandBorder.Child!).Children[1];
        await Assert.That(commandText.Text).IsEqualTo("$ ls");

        // Last child: stdout mono TextBlock with explicit theme foreground
        // (an Avalonia FluentTheme bug resolves a null Foreground inside a
        // ContentControl-wrapped StackPanel to transparent — see the
        // comment on BuildOutputBlock for context).
        var stdoutBlock = (TextBlock)body.Children[^1];
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
        var stderrBlock = (TextBlock)body.Children[^1];
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
        // 1 command row + 1 stdout + 1 stderr = 3 children
        await Assert.That(body.Children.Count).IsEqualTo(3);

        var stdout = (TextBlock)body.Children[1];
        var stderr = (TextBlock)body.Children[2];
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
        var hint = (TextBlock)body.Children[^1];
        await Assert.That(hint.Text).IsEqualTo("(no output)");
        await Assert.That(hint.Foreground).IsEqualTo(AvaloniaTheme.TextSecondary);
    }

    [Test]
    public async Task Complete_NoDetails_LegacyFallback_ShowsStdoutFromContent()
    {
        // Legacy transcripts: no Details payload — Details wasn't
        // persisted when this row was written. The body must fall back
        // to reading stdout / stderr from the Content textblocks so
        // the user still sees the actual output.
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls"));
        card.Complete(LegacyBashResult(stdout: "file1\nfile2", stderr: ""));

        var body = BodyOf(card);
        var stdoutBlock = (TextBlock)body.Children[^1];
        await Assert.That(stdoutBlock.Text).IsEqualTo("file1\nfile2");
    }

    [Test]
    public async Task Complete_NoDetails_LegacyFallback_ShowsStderrFromContent()
    {
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("false"));
        card.Complete(LegacyBashResult(stdout: "", stderr: "command not found", isError: true));

        var body = BodyOf(card);
        var stderrBlock = (TextBlock)body.Children[^1];
        await Assert.That(stderrBlock.Text).IsEqualTo("command not found");
        await Assert.That(stderrBlock.Foreground).IsEqualTo(AvaloniaTheme.Danger);
    }

    [Test]
    public async Task Complete_NoDetails_CommandFromArguments_FallsBackWhenDetailsMissing()
    {
        // When Details is null and Content textblocks are also empty,
        // command comes from the tool call arguments (still surfaced
        // so the user can see what was run, just without output).
        AvaloniaTestHost.EnsureInitialized();
        var card = new BashToolCardView();
        card.ShowPending(Call("ls"));
        card.Complete(LegacyBashResult(stdout: "", stderr: ""));

        var body = BodyOf(card);
        var commandBorder = (Border)body.Children[0];
        var commandText = (TextBlock)((DockPanel)commandBorder.Child!).Children[1];
        await Assert.That(commandText.Text).IsEqualTo("$ ls");
    }
}
