using Phi.Agent;

namespace Phi.Provider.Tests;

public class OpenAICompatibleProviderTests
{
    [Test]
    public async Task StreamResponseAsync_BasicChat_AccumulatesTextDeltas()
    {
        var handler = new FixtureHttpHandler("Fixtures/DeepSeekBasicChat.sse");
        var http = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider(
            new OpenAICompatibleConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.deepseek.com",
                Provider = "deepseek",
            },
            http);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "deepseek-v4-flash",
            system: "You are helpful",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []))
        {
            events.Add(ev);
        }

        var text = string.Concat(
            events.OfType<TextDeltaEvent>()
                .Select(t => t.Delta));
        await Assert.That(text).IsEqualTo("Hello! How can I help you today?");

        var end = events.OfType<AssistantDoneEvent>().Single();
        await Assert.That(string.Concat(events.OfType<TextDeltaEvent>().Select(t => t.Delta)))
            .IsEqualTo("Hello! How can I help you today?");
        await Assert.That(end.Message.Model).IsEqualTo("deepseek-v4-flash");
        await Assert.That(end.Message.Provider).IsEqualTo("deepseek");
        await Assert.That(end.FinishReason).IsEqualTo(StopReasons.Stop);
    }

    [Test]
    public async Task StreamResponseAsync_PostsToCorrectEndpointWithExpectedPayload()
    {
        var handler = new FixtureHttpHandler("Fixtures/DeepSeekBasicChat.sse");
        var http = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider(
            new OpenAICompatibleConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.deepseek.com",
                Provider = "deepseek",
            },
            http);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "deepseek-v4-flash",
            system: "system-prompt",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []));

        await Assert.That(handler.LastRequestUri)
            .IsEqualTo("https://api.deepseek.com/chat/completions");
        await Assert.That(handler.LastRequestBody).IsNotNull();
        await Assert.That(handler.LastRequestBody!).Contains("\"model\":\"deepseek-v4-flash\"");
        await Assert.That(handler.LastRequestBody!).Contains("\"stream\":true");
        await Assert.That(handler.LastRequestBody!).Contains("\"role\":\"system\"");
        await Assert.That(handler.LastRequestBody!).Contains("\"content\":\"system-prompt\"");
        await Assert.That(handler.LastRequestBody!).Contains("\"role\":\"user\"");
        await Assert.That(handler.LastRequestBody!).Contains("\"content\":\"Hi\"");
    }

    [Test]
    public async Task StreamResponseAsync_EmptyTextResponse_EmitsEmptyAssistantMessage()
    {
        // No text content, just an empty delta then [DONE]
        // Use a custom handler that returns a minimal SSE
        var handler = new InlineSseHandler("""
            data: {"id":"x","choices":[{"index":0,"delta":{"role":"assistant","content":""},"finish_reason":null}]}

            data: {"id":"x","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """);
        var http = new HttpClient(handler);
        var provider = new OpenAICompatibleProvider(
            new OpenAICompatibleConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.deepseek.com",
                Provider = "deepseek",
            },
            http);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "deepseek-v4-flash",
            system: "",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []))
        {
            events.Add(ev);
        }

        await Assert.That(events.OfType<TextDeltaEvent>()).IsEmpty();
        var end = events.OfType<AssistantDoneEvent>().Single();
        await Assert.That(end.Message.Text).IsEqualTo("");
        await Assert.That(end.Message.Content).IsEmpty();
        await Assert.That(end.FinishReason).IsEqualTo(StopReasons.Stop);
    }

    [Test]
    public async Task StreamResponseAsync_CalledTwiceWithSameTools_DoesNotThrowNodeHasParent()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        var handler = new InlineSseHandler(sse);
        var provider = new OpenAICompatibleProvider(
            new OpenAICompatibleConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.deepseek.com",
                Provider = "deepseek",
            },
            new HttpClient(handler));

        var tools = new List<Tool>
        {
            new StubTool("bash", "run a command", new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "object",
                ["properties"] = new System.Text.Json.Nodes.JsonObject(),
            }),
        };

        var first = await CollectEvents(provider.StreamResponseAsync(
            "m", "", [new UserMessage { Content = "hi" }], tools));
        var second = await CollectEvents(provider.StreamResponseAsync(
            "m", "", [new UserMessage { Content = "again" }], tools));

        await Assert.That(first.OfType<AssistantDoneEvent>().Count()).IsEqualTo(1);
        await Assert.That(second.OfType<AssistantDoneEvent>().Count()).IsEqualTo(1);
    }

    private static async Task<List<ProviderEvent>> CollectEvents(
        IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var ev in source) list.Add(ev);
        return list;
    }
}
