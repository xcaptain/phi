namespace Phi.Agent.Tests;

public class ProviderEventTests
{
    [Test]
    public async Task TextDelta_KindIsTextDelta()
    {
        var ev = new ProviderTextDeltaEvent("hi");

        await Assert.That(ev.Kind).IsEqualTo("TextDelta");
    }

    [Test]
    public async Task ThinkingStart_KindIsThinkingStart()
    {
        var ev = new ProviderThinkingStartEvent();

        await Assert.That(ev.Kind).IsEqualTo("ThinkingStart");
    }

    [Test]
    public async Task ThinkingDelta_KindIsThinkingDelta()
    {
        var ev = new ProviderThinkingDeltaEvent("reasoning about...");

        await Assert.That(ev.Kind).IsEqualTo("ThinkingDelta");
        await Assert.That(ev.Delta).IsEqualTo("reasoning about...");
    }

    [Test]
    public async Task ThinkingEnd_KindIsThinkingEnd()
    {
        var block = new ThinkingBlock("done");
        var ev = new ProviderThinkingEndEvent(block);

        await Assert.That(ev.Kind).IsEqualTo("ThinkingEnd");
        await Assert.That(ev.Block.Thinking).IsEqualTo("done");
    }

    [Test]
    public async Task ToolCall_KindIsToolCall()
    {
        var ev = new ProviderToolCallEvent(new ToolCall("c1", "bash"));

        await Assert.That(ev.Kind).IsEqualTo("ToolCall");
    }

    [Test]
    public async Task ResponseEnd_KindIsResponseEnd()
    {
        var ev = new ProviderResponseEndEvent(new AssistantMessage());

        await Assert.That(ev.Kind).IsEqualTo("ResponseEnd");
    }

    [Test]
    public async Task Error_KindIsError()
    {
        var ev = new ProviderErrorEvent("boom");

        await Assert.That(ev.Kind).IsEqualTo("Error");
    }
}
