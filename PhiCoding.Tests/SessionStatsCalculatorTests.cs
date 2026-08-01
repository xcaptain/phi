using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiCoding.Tests;

/// <summary>
/// <see cref="SessionStatsCalculator"/> aggregates cumulative session-level
/// numbers (turns, tool calls, billed tokens). The shape mirrors tau's
/// <c>calculate_session_stats</c>: pure function over the message list, no
/// side effects, deterministic.
/// </summary>
public class SessionStatsCalculatorTests
{
    private static Usage Tokens(int input, int output,
        int cacheRead = 0, int cacheWrite = 0) => new()
        {
            Input = input,
            Output = output,
            CacheRead = cacheRead,
            CacheWrite = cacheWrite,
            TotalTokens = input + output + cacheRead + cacheWrite,
        };

    private static AssistantMessage AssistantWithUsage(
        Usage usage, params ToolCall[] calls) => new()
        {
            Content = calls.Length > 0 ? calls.Cast<ContentBlock>().ToList() : [],
            Usage = usage,
            StopReason = calls.Length > 0 ? StopReasons.ToolUse : StopReasons.Stop,
        };

    // ──────────────────── Empty ────────────────────

    [Test]
    public async Task Empty_ReturnsZero()
    {
        var stats = SessionStatsCalculator.Calculate([]);

        await Assert.That(stats).IsEqualTo(SessionStats.Zero);
    }

    // ──────────────────── User-only ────────────────────

    [Test]
    public async Task UserMessages_Only_IncrementTurnCount()
    {
        var stats = SessionStatsCalculator.Calculate(
        [
            new UserMessage { Content = "hi" },
            new UserMessage { Content = "again" },
        ]);

        await Assert.That(stats.TurnCount).IsEqualTo(2);
        await Assert.That(stats.ToolCallCount).IsEqualTo(0);
        await Assert.That(stats.InputTokens).IsEqualTo(0);
        await Assert.That(stats.OutputTokens).IsEqualTo(0);
    }

    // ──────────────────── Assistant usage ────────────────────

    [Test]
    public async Task AssistantMessage_AccumulatesUsage()
    {
        var stats = SessionStatsCalculator.Calculate(
        [
            new UserMessage { Content = "hi" },
            AssistantWithUsage(Tokens(input: 100, output: 30, cacheRead: 20, cacheWrite: 5)),
        ]);

        await Assert.That(stats.InputTokens).IsEqualTo(100 + 20 + 5);
        await Assert.That(stats.OutputTokens).IsEqualTo(30);
        await Assert.That(stats.TotalTokens).IsEqualTo(155);
    }

    // ──────────────────── Tool calls ────────────────────

    [Test]
    public async Task AssistantMessage_CountsToolCalls()
    {
        var stats = SessionStatsCalculator.Calculate(
        [
            new UserMessage { Content = "go" },
            AssistantWithUsage(
                Tokens(10, 5),
                new ToolCall("a", "read") { Arguments = JsonNode.Parse("{}")!.AsObject() },
                new ToolCall("b", "bash") { Arguments = JsonNode.Parse("{}")!.AsObject() }),
        ]);

        await Assert.That(stats.ToolCallCount).IsEqualTo(2);
    }

    // ──────────────────── Mixed / multiple turns ────────────────────

    [Test]
    public async Task MixedMessages_AggregatesAcrossTurns()
    {
        var messages = new IAgentMessage[]
        {
            new UserMessage { Content = "first" },
            AssistantWithUsage(Tokens(50, 20)),
            new ToolResultMessage
            {
                ToolCallId = "a",
                ToolName = "read",
                Content = [new TextBlock("output")],
                IsError = false,
            },
            AssistantWithUsage(
                Tokens(80, 40, cacheRead: 10),
                new ToolCall("b", "edit") { Arguments = JsonNode.Parse("{}")!.AsObject() }),
            new UserMessage { Content = "second" },
            AssistantWithUsage(Tokens(60, 25)),
        };

        var stats = SessionStatsCalculator.Calculate(messages);

        await Assert.That(stats.TurnCount).IsEqualTo(2);
        await Assert.That(stats.ToolCallCount).IsEqualTo(1);
        await Assert.That(stats.InputTokens).IsEqualTo(50 + 80 + 10 + 60);
        await Assert.That(stats.OutputTokens).IsEqualTo(20 + 40 + 25);
    }
}
