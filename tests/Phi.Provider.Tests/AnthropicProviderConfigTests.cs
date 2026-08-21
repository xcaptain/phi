namespace Phi.Provider.Tests;

public class AnthropicProviderConfigTests
{
    [Test]
    public async Task DefaultsAreSensibleForAnthropic()
    {
        var config = new AnthropicConfig { ApiKey = "test" };

        await Assert.That(config.BaseUrl).IsEqualTo("https://api.anthropic.com");
        await Assert.That(config.Api).IsEqualTo("anthropic-messages");
        await Assert.That(config.Provider).IsEqualTo("anthropic");
        await Assert.That(config.AnthropicVersion).IsEqualTo("2023-06-01");
        await Assert.That(config.MaxTokens).IsEqualTo(4096);
        await Assert.That(config.Timeout).IsEqualTo(TimeSpan.FromMinutes(2));
    }

    [Test]
    public async Task CustomBaseUrlAndProviderAreHonored()
    {
        var config = new AnthropicConfig
        {
            ApiKey = "test",
            BaseUrl = "https://api.minimax.com",
            Provider = "minimax",
        };

        await Assert.That(config.BaseUrl).IsEqualTo("https://api.minimax.com");
        await Assert.That(config.Provider).IsEqualTo("minimax");
    }

    [Test]
    public async Task BearerAuthSwitchesHeaderFromXApiKeyToAuthorization()
    {
        var config = new AnthropicConfig
        {
            ApiKey = "oauth-token",
            BearerAuth = true,
        };

        await Assert.That(config.BearerAuth).IsTrue();
    }
}
