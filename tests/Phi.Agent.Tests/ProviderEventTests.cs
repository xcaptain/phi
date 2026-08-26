namespace Phi.Agent.Tests;

public class ProviderEventTests
{
    [Test]
    public async Task TextDelta_KindIsTextDelta()
    {
        var ev = new TextDeltaEvent("hi");

        await Assert.That(ev.Kind).IsEqualTo("TextDelta");
    }

    [Test]
    public async Task AssistantStart_KindIsStart()
    {
        var ev = new AssistantStartEvent();

        await Assert.That(ev.Kind).IsEqualTo("Start");
    }

    [Test]
    public async Task ThinkingDelta_KindIsThinkingDelta()
    {
        var ev = new ThinkingDeltaEvent("reasoning about...");

        await Assert.That(ev.Kind).IsEqualTo("ThinkingDelta");
        await Assert.That(ev.Delta).IsEqualTo("reasoning about...");
    }

    [Test]
    public async Task ThinkingEnd_KindIsThinkingEnd()
    {
        var block = new ThinkingBlock("done");
        var ev = new ThinkingEndEvent(block);

        await Assert.That(ev.Kind).IsEqualTo("ThinkingEnd");
        await Assert.That(ev.Block.Thinking).IsEqualTo("done");
    }

    [Test]
    public async Task ToolCall_KindIsToolCall()
    {
        var ev = new ToolCallEvent(new ToolCall("c1", "bash"));

        await Assert.That(ev.Kind).IsEqualTo("ToolCall");
    }

    [Test]
    public async Task Done_KindIsDone()
    {
        var ev = new AssistantDoneEvent(new AssistantMessage());

        await Assert.That(ev.Kind).IsEqualTo("Done");
    }

    [Test]
    public async Task Error_KindIsError()
    {
        var ev = new AssistantErrorEvent("boom");

        await Assert.That(ev.Kind).IsEqualTo("Error");
    }
}
