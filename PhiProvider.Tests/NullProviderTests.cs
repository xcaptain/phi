using PhiAgent;

namespace PhiProvider.Tests;

public class NullProviderTests
{
    [Test]
    public async Task Stream_EmitsProviderErrorPointingAtConnect()
    {
        var provider = new NullProvider();
        var events = new List<ProviderEvent>();

        await foreach (var ev in provider.StreamResponseAsync(
            "m", "system", new List<IAgentMessage>(), new List<Tool>()))
        {
            events.Add(ev);
        }

        var error = events.Single();
        await Assert.That(error).IsTypeOf<ProviderErrorEvent>();
        await Assert.That(((ProviderErrorEvent)error).Message).Contains("/connect");
    }

    [Test]
    public async Task Stream_EmitsNoResponseEnd()
    {
        var provider = new NullProvider();
        var events = new List<ProviderEvent>();

        await foreach (var ev in provider.StreamResponseAsync(
            "m", "system", new List<IAgentMessage>(), new List<Tool>()))
        {
            events.Add(ev);
        }

        await Assert.That(events.OfType<ProviderResponseEndEvent>()).IsEmpty();
    }

    [Test]
    public void Dispose_IsSafeNoOp()
    {
        var provider = new NullProvider();
        provider.Dispose();
        provider.Dispose();
    }
}
