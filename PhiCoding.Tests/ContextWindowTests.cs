using PhiAgent;

namespace PhiCoding.Tests;

public class ContextWindowTests
{
    [Test]
    public async Task EstimateTextTokens_Empty_IsZero()
    {
        await Assert.That(ContextWindow.EstimateTextTokens(null)).IsEqualTo(0);
        await Assert.That(ContextWindow.EstimateTextTokens("")).IsEqualTo(0);
    }

    [Test]
    public async Task EstimateTextTokens_NonEmpty_ApproximatesFourCharsPerToken()
    {
        // 12 chars / 4 = 3 tokens (rounded up).
        await Assert.That(ContextWindow.EstimateTextTokens("hello world!")).IsEqualTo(3);
        // 1 char still counts as 1 token.
        await Assert.That(ContextWindow.EstimateTextTokens("a")).IsEqualTo(1);
    }

    [Test]
    public async Task EstimateMessageTokens_IncludesOverhead()
    {
        var msg = new UserMessage { Content = "abcd" };
        // 1 token for text + 4 overhead = 5
        await Assert.That(ContextWindow.EstimateMessageTokens(msg)).IsEqualTo(5);
    }

    [Test]
    public async Task EstimateMessageTokens_AssistantWithToolCalls_AddsArgumentsTokens()
    {
        var args = new System.Text.Json.Nodes.JsonObject
        {
            ["path"] = "long/very/long/file/path/that/should/consume/tokens",
        };
        var msg = new AssistantMessage
        {
            Content =
            [
                new TextBlock("ok"),
                new ToolCall("c1", "read") { Arguments = args },
            ],
            StopReason = StopReasons.ToolUse,
        };
        var tokens = ContextWindow.EstimateMessageTokens(msg);
        // text(1) + overhead(4) + name "read"(1) + args JSON(>= 1) >= 7
        await Assert.That(tokens).IsGreaterThanOrEqualTo(7);
    }

    [Test]
    public async Task AutoCompactionThreshold_ReturnsWindowMinusReserve()
    {
        var threshold = ContextWindow.AutoCompactionThresholdForContextWindow(128_000);
        await Assert.That(threshold).IsEqualTo(128_000 - ContextWindow.DefaultCompactionReserveTokens);
    }

    [Test]
    public async Task AutoCompactionThreshold_ZeroOrNegativeWindow_ReturnsNull()
    {
        await Assert.That(ContextWindow.AutoCompactionThresholdForContextWindow(0)).IsNull();
        await Assert.That(ContextWindow.AutoCompactionThresholdForContextWindow(-1)).IsNull();
    }
}
