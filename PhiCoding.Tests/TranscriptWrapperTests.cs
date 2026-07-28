using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class TranscriptWrapperTests
{
    [Test]
    public async Task Wrap_LongLine_SplitsAtWidth()
    {
        var item = new ChatItem(ChatItemKind.Assistant, TranscriptStyle.Assistant);
        item.Text.Append("abcdefghij"); // 10 chars, width 4 -> 3 lines

        var lines = TranscriptWrapper.Wrap([item], 4);
        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(lines[0].Text).IsEqualTo("abcd");
        await Assert.That(lines[2].Text).IsEqualTo("ij");
        await Assert.That(lines[0].Style).IsEqualTo(TranscriptStyle.Assistant);
    }

    [Test]
    public async Task Wrap_UserItem_GetsPromptPrefix()
    {
        var item = new ChatItem(ChatItemKind.User, TranscriptStyle.User);
        item.Text.Append("hello\nworld");

        var lines = TranscriptWrapper.Wrap([item], 80);
        await Assert.That(lines[0].Text).IsEqualTo("> hello");
        await Assert.That(lines[1].Text).IsEqualTo("  world");
    }

    [Test]
    public async Task Wrap_MultipleItems_SeparatedByBlankLine()
    {
        var a = new ChatItem(ChatItemKind.User, TranscriptStyle.User);
        a.Text.Append("q");
        var b = new ChatItem(ChatItemKind.Assistant, TranscriptStyle.Assistant);
        b.Text.Append("a");

        var lines = TranscriptWrapper.Wrap([a, b], 80);
        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(lines[1].Text).IsEqualTo("");
    }

    [Test]
    public async Task Wrap_StyledLines_KeptVerbatim()
    {
        var item = new ChatItem(ChatItemKind.Tool, TranscriptStyle.ToolCall)
        {
            StyledLines = [new TranscriptLine("$ ls", TranscriptStyle.ToolCall)],
        };

        var lines = TranscriptWrapper.Wrap([item], 80);
        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0].Style).IsEqualTo(TranscriptStyle.ToolCall);
    }

    [Test]
    public async Task Wrap_EmptyAssistantItem_RendersSingleBlankLine()
    {
        var item = new ChatItem(ChatItemKind.Assistant, TranscriptStyle.Assistant);
        var lines = TranscriptWrapper.Wrap([item], 80);
        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0].Text).IsEqualTo("");
    }
}
