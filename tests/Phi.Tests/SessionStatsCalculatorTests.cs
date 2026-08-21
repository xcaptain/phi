using Phi.Agent;

namespace Phi.Tests;

public class SessionStatsCalculatorTests
{
    [Test]
    public async Task Calculate_Empty_ReturnsZero()
    {
        var stats = SessionStatsCalculator.Calculate([]);
        await Assert.That(stats).IsEqualTo(SessionStats.Zero);
    }

    [Test]
    public async Task Calculate_AssistantMessage_AccumulatesUsage()
    {
        var stats = SessionStatsCalculator.Calculate(
        [
            new AssistantMessage
            {
                Content = [new TextBlock("hi")],
                StopReason = StopReasons.Stop,
                Usage = new Usage { Input = 10, Output = 5, TotalTokens = 15 },
            },
        ]);
        await Assert.That(stats.InputTokens).IsEqualTo(10);
        await Assert.That(stats.OutputTokens).IsEqualTo(5);
        await Assert.That(stats.TotalTokens).IsEqualTo(15);
    }

    [Test]
    public async Task Calculate_CompactionPrefixUserMessage_DoesNotCountAsTurn()
    {
        // The compaction-summary user message rides along at index 0; it
        // must not inflate TurnCount, otherwise the UI would report
        // phantom user turns after every compaction.
        var stats = SessionStatsCalculator.Calculate(
        [
            new UserMessage { Content = ContextWindow.CompactionSummaryPrefix + "summary" },
            new UserMessage { Content = "real user turn" },
            new AssistantMessage { Content = [new TextBlock("reply")], StopReason = StopReasons.Stop },
        ]);
        await Assert.That(stats.TurnCount).IsEqualTo(1);
    }

    [Test]
    public async Task Calculate_MixedMessages_AggregatesAcrossTurns()
    {
        // Multi-turn scenario: user/assistant/tool-result interleavings
        // plus cache_read on one assistant. Covers the full aggregation
        // path (turnCount, toolCallCount, input/output/cache/total tokens)
        // in a single test so a regression in any of those fields shows up
        // loudly instead of silently undercounting billed totals.
        var stats = SessionStatsCalculator.Calculate(
        [
            new UserMessage { Content = "first" },
            new AssistantMessage
            {
                Content = [new TextBlock("a1")],
                StopReason = StopReasons.Stop,
                Usage = new Usage { Input = 50, Output = 20, TotalTokens = 70 },
            },
            new ToolResultMessage
            {
                ToolCallId = "t1", ToolName = "read",
                Content = [new TextBlock("output")],
                IsError = false,
            },
            new AssistantMessage
            {
                Content = [new TextBlock("a2"), new ToolCall("t2", "edit")],
                StopReason = StopReasons.ToolUse,
                Usage = new Usage
                {
                    Input = 80, Output = 40, CacheRead = 10, TotalTokens = 130,
                },
            },
            new UserMessage { Content = "second" },
            new AssistantMessage
            {
                Content = [new TextBlock("a3")],
                StopReason = StopReasons.Stop,
                Usage = new Usage { Input = 60, Output = 25, TotalTokens = 85 },
            },
        ]);

        await Assert.That(stats.TurnCount).IsEqualTo(2);
        await Assert.That(stats.ToolCallCount).IsEqualTo(1);
        // InputTokens = (50+0+0) + (80+10+0) + (60+0+0) = 200
        await Assert.That(stats.InputTokens).IsEqualTo(200);
        // OutputTokens = 20 + 40 + 25 = 85
        await Assert.That(stats.OutputTokens).IsEqualTo(85);
        // TotalTokens = 70 + 130 + 85 = 285
        await Assert.That(stats.TotalTokens).IsEqualTo(285);
    }

    [Test]
    public async Task WithAddedUsage_NullExtra_ReturnsOriginal()
    {
        var stats = new SessionStats(2, 5, 100, 50, 150, null);
        var result = SessionStatsCalculator.WithAddedUsage(stats, null);
        await Assert.That(result).IsEqualTo(stats);
    }

    [Test]
    public async Task WithAddedUsage_FoldsCacheAndInputTokens()
    {
        var stats = new SessionStats(2, 5, 100, 50, 150, null);
        var extra = new Usage
        {
            Input = 10,
            Output = 5,
            TotalTokens = 15,
            CacheRead = 20,
            CacheWrite = 5,
        };
        var result = SessionStatsCalculator.WithAddedUsage(stats, extra);
        // InputTokens = 100 + 10 (extra.Input) + 20 (extra.CacheRead) + 5 (extra.CacheWrite) = 135
        await Assert.That(result.InputTokens).IsEqualTo(135);
        await Assert.That(result.OutputTokens).IsEqualTo(55);
        await Assert.That(result.TotalTokens).IsEqualTo(165);
    }
}
