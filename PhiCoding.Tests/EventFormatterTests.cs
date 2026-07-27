using PhiAgent;
using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class EventFormatterTests
{
    [Test]
    public async Task Format_TurnStart_IncludesTurnNumber()
    {
        var result = EventFormatter.Format(new TurnStartEvent(3));
        await Assert.That(result).Contains("turn 3");
    }

    [Test]
    public async Task Format_TextDelta_ReturnsRawDelta()
    {
        var result = EventFormatter.Format(new AssistantTextDeltaEvent("hello"));
        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task Format_ToolCall_IncludesNameAndId()
    {
        var result = EventFormatter.Format(new AssistantToolCallEvent(
            new ToolCall("call_abc", "bash")));
        await Assert.That(result).Contains("bash");
        await Assert.That(result).Contains("call_abc");
    }

    [Test]
    public async Task Format_ToolExecutionEnd_TruncatesLongOutput()
    {
        var longOutput = new string('x', 1000);
        var result = EventFormatter.Format(new ToolExecutionEndEvent(
            new ToolCall("c1", "bash"),
            new ToolResult([new TextBlock(longOutput)])));
        await Assert.That(result.Length).IsLessThan(1000);
        await Assert.That(result).Contains("...");
    }

    [Test]
    public async Task Format_ToolExecutionEnd_KeepsShortOutput()
    {
        var result = EventFormatter.Format(new ToolExecutionEndEvent(
            new ToolCall("c1", "bash"),
            new ToolResult([new TextBlock("ok")])));
        await Assert.That(result).Contains("ok");
        await Assert.That(result).DoesNotContain("...");
    }

    [Test]
    public async Task Format_TurnEnd_IncludesStopReason()
    {
        var result = EventFormatter.Format(new TurnEndEvent(
            new AssistantMessage { StopReason = StopReasons.ToolUse }));
        await Assert.That(result).Contains("toolUse");
    }

    [Test]
    public async Task Format_ToolExecutionStart_IncludesToolName()
    {
        var result = EventFormatter.Format(new ToolExecutionStartEvent("c1", "read"));
        await Assert.That(result).Contains("read");
    }
}