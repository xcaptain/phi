using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class ChatTranscriptTests
{
    [Test]
    public async Task FormatThinkingText_SingleLine_WrapsWithDim()
    {
        var result = ChatTranscript.FormatThinkingText("Step 1: read the file");

        await Assert.That(result).IsEqualTo("[dim]Step 1: read the file[/]");
    }

    [Test]
    public async Task FormatThinkingText_MultiLine_WrapsEachLine()
    {
        var result = ChatTranscript.FormatThinkingText("Step 1: read\nStep 2: edit");

        await Assert.That(result).IsEqualTo(
            "[dim]Step 1: read[/]\n[dim]Step 2: edit[/]");
    }

    [Test]
    public async Task FormatThinkingText_BracketCharacters_AreEscaped()
    {
        // The model may emit [dim] or [bold] literally in its thinking;
        // they must be escaped so the markup parser doesn't interpret them.
        var result = ChatTranscript.FormatThinkingText("Use [bold] markup carefully");

        await Assert.That(result).IsEqualTo(
            "[dim]Use \\[bold\\] markup carefully[/]");
    }

    [Test]
    public async Task FormatThinkingText_CrlfLineEndings_AreNormalized()
    {
        var result = ChatTranscript.FormatThinkingText("Line A\r\nLine B");

        await Assert.That(result).IsEqualTo(
            "[dim]Line A[/]\n[dim]Line B[/]");
    }

    [Test]
    public async Task FormatThinkingText_EmptyString_YieldsEmptyWrapper()
    {
        var result = ChatTranscript.FormatThinkingText("");

        await Assert.That(result).IsEqualTo("[dim][/]");
    }

    [Test]
    public async Task FormatThinkingDuration_SubSecond_AsMilliseconds()
    {
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMilliseconds(0)))
            .IsEqualTo("0ms");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMilliseconds(500)))
            .IsEqualTo("500ms");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMilliseconds(999)))
            .IsEqualTo("999ms");
    }

    [Test]
    public async Task FormatThinkingDuration_SubMinute_AsSecondsWithDecimal()
    {
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(1)))
            .IsEqualTo("1.0s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMilliseconds(1500)))
            .IsEqualTo("1.5s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(45)))
            .IsEqualTo("45.0s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(59.9)))
            .IsEqualTo("59.9s");
    }

    [Test]
    public async Task FormatThinkingDuration_OverMinute_AsMinutesAndSeconds()
    {
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(60)))
            .IsEqualTo("1m0s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(125)))
            .IsEqualTo("2m5s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(42)))
            .IsEqualTo("3m42s");
    }
}