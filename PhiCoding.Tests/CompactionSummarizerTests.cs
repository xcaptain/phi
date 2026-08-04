using PhiAgent;
using PhiCoding.Tests.Helpers;

namespace PhiCoding.Tests;

public class CompactionSummarizerTests
{
    [Test]
    public async Task BuildPrompt_NoPreviousSummary_UsesCreatePrompt()
    {
        var prompt = CompactionSummarizer.BuildPrompt(
        [
            new UserMessage { Content = "hi" },
            new AssistantMessage { Content = [new TextBlock("hello")], StopReason = StopReasons.Stop },
        ]);

        await Assert.That(prompt).Contains(CompactionSummarizer.SummarizationPrompt);
        await Assert.That(prompt).DoesNotContain(CompactionSummarizer.UpdateSummarizationPrompt);
    }

    [Test]
    public async Task BuildPrompt_HasPreviousSummary_UsesUpdatePrompt()
    {
        var previousSummary = "## Goal\nFinish the migration";
        var firstMessage = ContextWindow.CompactionSummaryPrefix + previousSummary;
        var prompt = CompactionSummarizer.BuildPrompt(
        [
            new UserMessage { Content = firstMessage },
            new UserMessage { Content = "follow-up" },
            new AssistantMessage { Content = [new TextBlock("ok")], StopReason = StopReasons.Stop },
        ]);

        await Assert.That(prompt).Contains(CompactionSummarizer.UpdateSummarizationPrompt);
        await Assert.That(prompt).Contains(previousSummary);
    }

    [Test]
    public async Task GenerateSummary_EchoProvider_ReturnsTextTurn()
    {
        var summaryEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("condensed"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("condensed")],
                StopReason = StopReasons.Stop,
            }),
        };
        var provider = StubProvider.Echo(summaryEvents);
        var result = await CompactionSummarizer.GenerateAsync(
            provider, "stub-model",
            [
                new UserMessage { Content = "old" },
                new AssistantMessage { Content = [new TextBlock("old assistant")], StopReason = StopReasons.Stop },
            ]);
        await Assert.That(result.Text).IsEqualTo("condensed");
        // The summarization itself shouldn't leak the original messages into stats —
        // we just confirm here that the provider was called once.
        await Assert.That(provider.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task GenerateSummary_CapturesUsageFromResponseEnd()
    {
        var summaryEvents = new ProviderEvent[]
        {
            new ProviderTextDeltaEvent("summary"),
            new ProviderResponseEndEvent(new AssistantMessage
            {
                Content = [new TextBlock("summary")],
                StopReason = StopReasons.Stop,
                Usage = new Usage
                {
                    Input = 100, Output = 50, TotalTokens = 150,
                    CacheRead = 20, CacheWrite = 10,
                },
            }),
        };
        var provider = StubProvider.Echo(summaryEvents);
        var result = await CompactionSummarizer.GenerateAsync(
            provider, "stub-model",
            [new UserMessage { Content = "old" }]);
        await Assert.That(result.Usage.Input).IsEqualTo(100);
        await Assert.That(result.Usage.Output).IsEqualTo(50);
        await Assert.That(result.Usage.CacheRead).IsEqualTo(20);
        await Assert.That(result.Usage.CacheWrite).IsEqualTo(10);
        await Assert.That(result.Usage.TotalTokens).IsEqualTo(150);
    }

    [Test]
    public async Task BuildPrompt_PiFormat_UserAndAssistantLines()
    {
        var prompt = CompactionSummarizer.BuildPrompt(
        [
            new UserMessage { Content = "hi" },
            new AssistantMessage { Content = [new TextBlock("hello")], StopReason = StopReasons.Stop },
        ]);
        await Assert.That(prompt).Contains("[User]: hi");
        await Assert.That(prompt).Contains("[Assistant]: hello");
        // Old XML format must not leak through.
        await Assert.That(prompt).DoesNotContain("<message ");
        await Assert.That(prompt).DoesNotContain("</message>");
    }

    [Test]
    public async Task BuildPrompt_PiFormat_ToolCallsInline()
    {
        var prompt = CompactionSummarizer.BuildPrompt(
        [
            new UserMessage { Content = "look at foo" },
            new AssistantMessage
            {
                Content =
                [
                    new TextBlock("reading"),
                    new ToolCall("t1", "read") { Arguments = new() { ["path"] = "foo.ts" } },
                ],
                StopReason = StopReasons.ToolUse,
            },
            new ToolResultMessage
            {
                ToolCallId = "t1", ToolName = "read",
                Content = [new TextBlock("contents")],
            },
        ]);
        await Assert.That(prompt).Contains("[Assistant tool calls]: read(");
        await Assert.That(prompt).Contains("\"path\":\"foo.ts\"");
        await Assert.That(prompt).Contains("[Tool result] (read): contents");
    }

    [Test]
    public async Task BuildPrompt_PiFormat_AssistantThinkingLine()
    {
        var prompt = CompactionSummarizer.BuildPrompt(
        [
            new UserMessage { Content = "go" },
            new AssistantMessage
            {
                Content =
                [
                    new ThinkingBlock("reasoning..."),
                    new TextBlock("answer"),
                ],
                StopReason = StopReasons.Stop,
            },
        ]);
        await Assert.That(prompt).Contains("[Assistant thinking]: reasoning...");
        await Assert.That(prompt).Contains("[Assistant]: answer");
    }

    [Test]
    public async Task BuildPrompt_LongToolResult_TruncatesWithMarker()
    {
        var big = new string('x', CompactionSummarizer.ToolResultTruncateChars + 500);
        var prompt = CompactionSummarizer.BuildPrompt(
        [
            new UserMessage { Content = "go" },
            new ToolResultMessage
            {
                ToolCallId = "t1", ToolName = "read",
                Content = [new TextBlock(big)],
            },
        ]);
        await Assert.That(prompt).Contains("[Tool result] (read): " + new string('x', CompactionSummarizer.ToolResultTruncateChars));
        await Assert.That(prompt).Contains("[...truncated 500 chars]");
        await Assert.That(prompt).DoesNotContain(new string('x', CompactionSummarizer.ToolResultTruncateChars + 1));
    }

    [Test]
    public async Task BuildPrompt_ShortToolResult_NotTruncated()
    {
        var prompt = CompactionSummarizer.BuildPrompt(
        [
            new UserMessage { Content = "go" },
            new ToolResultMessage
            {
                ToolCallId = "t1", ToolName = "read",
                Content = [new TextBlock("tiny output")],
            },
        ]);
        await Assert.That(prompt).Contains("[Tool result] (read): tiny output");
        await Assert.That(prompt).DoesNotContain("[...truncated");
    }

    [Test]
    public async Task BuildPrompt_WithPreviousDetails_AppendsFileSections()
    {
        var details = new CompactionDetails(
            ReadFiles: ["src/a.ts", "src/b.ts"],
            ModifiedFiles: ["src/c.ts"]);
        var prompt = CompactionSummarizer.BuildPrompt(
            [new UserMessage { Content = "go" }],
            previousDetails: details);
        await Assert.That(prompt).Contains("<read-files>");
        await Assert.That(prompt).Contains("src/a.ts");
        await Assert.That(prompt).Contains("src/b.ts");
        await Assert.That(prompt).Contains("</read-files>");
        await Assert.That(prompt).Contains("<modified-files>");
        await Assert.That(prompt).Contains("src/c.ts");
        await Assert.That(prompt).Contains("</modified-files>");
    }

    [Test]
    public async Task BuildPrompt_NoPreviousDetails_NoFileSections()
    {
        var prompt = CompactionSummarizer.BuildPrompt(
            [new UserMessage { Content = "go" }]);
        await Assert.That(prompt).DoesNotContain("<read-files>");
        await Assert.That(prompt).DoesNotContain("<modified-files>");
    }

    [Test]
    public async Task BuildPrompt_EmptyPreviousDetails_NoFileSections()
    {
        var prompt = CompactionSummarizer.BuildPrompt(
            [new UserMessage { Content = "go" }],
            previousDetails: CompactionDetails.Empty);
        await Assert.That(prompt).DoesNotContain("<read-files>");
        await Assert.That(prompt).DoesNotContain("<modified-files>");
    }

    [Test]
    public async Task BuildPrompt_WithTurnPrefix_SerializesBothRanges()
    {
        var history = new List<IAgentMessage>
        {
            new UserMessage { Content = "old user" },
            new AssistantMessage { Content = [new TextBlock("old assistant")], StopReason = StopReasons.Stop },
        };
        var turnPrefix = new List<IAgentMessage>
        {
            new UserMessage { Content = "new user (current turn)" },
            new AssistantMessage { Content = [new TextBlock("new assistant (current turn)")], StopReason = StopReasons.Stop },
        };
        var prompt = CompactionSummarizer.BuildPrompt(
            history,
            turnPrefixMessages: turnPrefix);
        // History inside <conversation>...
        await Assert.That(prompt).Contains("<conversation>");
        await Assert.That(prompt).Contains("[User]: old user");
        await Assert.That(prompt).Contains("[Assistant]: old assistant");
        // ...then turn prefix in its own block.
        await Assert.That(prompt).Contains("[Current turn — early portion]");
        await Assert.That(prompt).Contains("[User]: new user (current turn)");
        await Assert.That(prompt).Contains("[Assistant]: new assistant (current turn)");
        await Assert.That(prompt).Contains("[/Current turn — early portion]");
    }

    [Test]
    public async Task BuildPrompt_NoTurnPrefix_NoSplitTurnBlock()
    {
        var prompt = CompactionSummarizer.BuildPrompt(
            [new UserMessage { Content = "go" }]);
        await Assert.That(prompt).DoesNotContain("[Current turn — early portion]");
    }
}
