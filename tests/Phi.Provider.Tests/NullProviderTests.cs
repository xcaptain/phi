using Phi.Agent;

namespace Phi.Provider.Tests;

public class NullProviderTests
{
    [Test]
    public async Task Stream_EmitsProviderErrorPointingAtConnect()
    {
        var provider = new NullProvider();
        var events = new List<ProviderEvent>();

        await foreach (var ev in provider.StreamResponseAsync(
            "m", "system", [], []))
        {
            events.Add(ev);
        }

        var error = events.Single();
        await Assert.That(error).IsTypeOf<AssistantErrorEvent>();
        await Assert.That(((AssistantErrorEvent)error).Message).Contains("/connect");
    }

    [Test]
    public async Task Stream_EmitsNoResponseEnd()
    {
        var provider = new NullProvider();
        var events = new List<ProviderEvent>();

        await foreach (var ev in provider.StreamResponseAsync(
            "m", "system", [], []))
        {
            events.Add(ev);
        }

        await Assert.That(events.OfType<AssistantDoneEvent>()).IsEmpty();
    }

    [Test]
    public void Dispose_IsSafeNoOp()
    {
        var provider = new NullProvider();
        provider.Dispose();
        provider.Dispose();
    }
}
