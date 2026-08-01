using PhiAgent;
using PhiCoding.Tests.Helpers;

namespace PhiCoding.Tests;

public class CompactionSummarizerTests
{
    [Test]
    public async Task BuildPrompt_NoPreviousSummary_UsesCreatePrompt()
    {
        var summarizer = new CompactionSummarizer();
        var prompt = summarizer.BuildPrompt(
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
        var summarizer = new CompactionSummarizer();
        var previousSummary = "## Goal\nFinish the migration";
        var firstMessage = ContextWindow.CompactionSummaryPrefix + previousSummary;
        var prompt = summarizer.BuildPrompt(
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
        var summarizer = new CompactionSummarizer();
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
        var summary = await summarizer.GenerateAsync(
            provider, "stub-model",
            [
                new UserMessage { Content = "old" },
                new AssistantMessage { Content = [new TextBlock("old assistant")], StopReason = StopReasons.Stop },
            ]);
        await Assert.That(summary).IsEqualTo("condensed");
        // The summarization itself shouldn't leak the original messages into stats —
        // we just confirm here that the provider was called once.
        await Assert.That(provider.CallCount).IsEqualTo(1);
    }
}