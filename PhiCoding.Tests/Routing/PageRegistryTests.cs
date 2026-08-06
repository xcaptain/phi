using PhiCoding.Tui.Pages;
using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Tests.Helpers;

namespace PhiCoding.Tests.Routing;

/// <summary>
/// <see cref="PageRegistry"/>: the route→page resolution algorithm. Every
/// route family resolves to its page; unknown routes are rejected loudly.
/// </summary>
public class PageRegistryTests
{
    private static readonly PageRegistry Registry = new();

    private static FakeSessionNavigator Navigator() =>
        new(new MockSession());

    [Test]
    public async Task NewSessionRequest_ResolvesToNewSessionPage()
    {
        var page = Registry.Resolve(
            new ChatRoute(new NewSessionRequest()), Navigator(), new ProviderManager());

        await Assert.That(page).IsTypeOf<NewSessionPage>();
    }

    [Test]
    public async Task ExistingSessionRequest_ResolvesToSessionPage()
    {
        var page = Registry.Resolve(
            new ChatRoute(new ExistingSessionRequest("abc")), Navigator(), new ProviderManager());

        await Assert.That(page).IsTypeOf<SessionPage>();
    }

    [Test]
    public async Task UnknownRoute_ThrowsNotSupported()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            Registry.Resolve(new UnknownRoute(), Navigator(), new ProviderManager()));

        await Assert.That(ex!.Message).Contains("UnknownRoute");
    }

    /// <summary>A route family with no registered page.</summary>
    private sealed record UnknownRoute : AppRoute;
}
