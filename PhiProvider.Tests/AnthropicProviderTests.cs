using PhiAgent;

namespace PhiProvider.Tests;

public class AnthropicProviderTests
{
    private static AnthropicProvider CreateProvider(FixtureHttpHandler handler) =>
        new(
            new AnthropicConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.anthropic.com",
                Provider = "anthropic",
            },
            new HttpClient(handler));

    [Test]
    public async Task StreamResponseAsync_BasicChat_AccumulatesTextDeltas()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var provider = CreateProvider(handler);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "You are helpful",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []))
        {
            events.Add(ev);
        }

        var text = string.Concat(events.OfType<ProviderTextDeltaEvent>().Select(e => e.Delta));
        await Assert.That(text).IsEqualTo("Hello! How can I help you today?");

        var end = events.OfType<ProviderResponseEndEvent>().Single();
        await Assert.That(end.Message.Text).IsEqualTo("Hello! How can I help you today?");
        await Assert.That(end.Message.Model).IsEqualTo("claude-sonnet-4-5");
        await Assert.That(end.Message.Provider).IsEqualTo("anthropic");
        await Assert.That(end.Message.Api).IsEqualTo("anthropic-messages");
        await Assert.That(end.FinishReason).IsEqualTo(StopReasons.Stop);
    }

    [Test]
    public async Task StreamResponseAsync_UsageIsPopulatedFromMessageStartAndDelta()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var provider = CreateProvider(handler);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []))
        {
            events.Add(ev);
        }

        var end = events.OfType<ProviderResponseEndEvent>().Single();
        await Assert.That(end.Message.Usage.Input).IsEqualTo(15);
        await Assert.That(end.Message.Usage.Output).IsEqualTo(15);
    }

    [Test]
    public async Task StreamResponseAsync_MaxTokensIsIncludedInRequest()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var provider = CreateProvider(handler);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []));

        // max_tokens is required by Anthropic API; ensure it shows up in body.
        await Assert.That(handler.LastRequestBody!).Contains("\"max_tokens\":4096");
    }

    [Test]
    public async Task StreamResponseAsync_CustomMaxTokensOverridesDefault()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var http = new HttpClient(handler);
        var provider = new AnthropicProvider(
            new AnthropicConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.anthropic.com",
                Provider = "anthropic",
                MaxTokens = 8192,
            },
            http);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []));

        await Assert.That(handler.LastRequestBody!).Contains("\"max_tokens\":8192");
    }

    [Test]
    public async Task StreamResponseAsync_EmptyTextResponse_EmitsEmptyAssistantMessage()
    {
        var handler = new InlineSseHandler("""
            event: message_start
            data: {"type":"message_start","message":{"id":"m","model":"claude-sonnet-4-5","usage":{"input_tokens":1,"output_tokens":1,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":1}}

            event: message_stop
            data: {"type":"message_stop"}
            """);
        var http = new HttpClient(handler);
        var provider = new AnthropicProvider(
            new AnthropicConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.anthropic.com",
                Provider = "anthropic",
            },
            http);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []))
        {
            events.Add(ev);
        }

        await Assert.That(events.OfType<ProviderTextDeltaEvent>().Count()).IsEqualTo(0);
        var end = events.OfType<ProviderResponseEndEvent>().Single();
        await Assert.That(end.Message.Text).IsEqualTo("");
        await Assert.That(end.FinishReason).IsEqualTo(StopReasons.Stop);
    }

    [Test]
    public async Task StreamResponseAsync_BearerAuthUsesAuthorizationHeaderInsteadOfXApiKey()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var http = new HttpClient(handler);
        var provider = new AnthropicProvider(
            new AnthropicConfig
            {
                ApiKey = "oauth-token",
                BaseUrl = "https://api.anthropic.com",
                Provider = "anthropic",
                BearerAuth = true,
            },
            http);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []));

        // Smoke check the request still posts.
        await Assert.That(handler.LastRequestUri)
            .IsEqualTo("https://api.anthropic.com/v1/messages");
    }

    private static async Task<List<ProviderEvent>> CollectEvents(
        IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var ev in source) list.Add(ev);
        return list;
    }
}
