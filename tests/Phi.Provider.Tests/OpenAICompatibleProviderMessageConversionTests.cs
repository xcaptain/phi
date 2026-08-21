using System.Text.Json.Nodes;
using Phi.Agent;

namespace Phi.Provider.Tests;

public class OpenAICompatibleProviderMessageConversionTests
{
    private static OpenAICompatibleProvider CreateProvider(FixtureHttpHandler handler) =>
        new(
            new OpenAICompatibleConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.deepseek.com",
                Provider = "deepseek",
            },
            new HttpClient(handler));

    [Test]
    public async Task StreamResponseAsync_AssistantMessageWithToolCalls_SerializesToolCallsInRequestBody()
    {
        var handler = new FixtureHttpHandler("Fixtures/DeepSeekBasicChat.sse");
        var provider = CreateProvider(handler);

        var assistant = new AssistantMessage
        {
            Content = [new TextBlock("Let me check."), new ToolCall("c1", "bash")
            {
                Arguments = JsonNode.Parse("""{"command":"ls"}""")!.AsObject(),
            }],
            StopReason = StopReasons.ToolUse,
        };

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "deepseek-v4-flash",
            system: "you are helpful",
            messages: [
                new UserMessage { Content = "Check system" },
                assistant,
                new ToolResultMessage
                {
                    ToolCallId = "c1",
                    ToolName = "bash",
                    Content = [new TextBlock("ok output")],
                },
            ],
            tools: []));

        await Assert.That(handler.LastRequestBody).IsNotNull();
        var body = handler.LastRequestBody!;

        // The assistant message must include tool_calls, otherwise the
        // following tool message is orphaned (OpenAI 400).
        await Assert.That(body).Contains("\"role\":\"assistant\"");
        await Assert.That(body).Contains("\"tool_calls\":[");
        await Assert.That(body).Contains("\"id\":\"c1\"");
        await Assert.That(body).Contains("\"name\":\"bash\"");
        await Assert.That(body).Contains("\"arguments\":");
        // The arguments field must be a JSON-encoded STRING (not object),
        // and contain the original command. System.Text.Json default
        // escapes " as \u0022 — assert structural pieces rather than
        // literal escaping.
        await Assert.That(body).Contains("command");
        await Assert.That(body).Contains("ls");

        // And the tool message must follow.
        await Assert.That(body).Contains("\"role\":\"tool\"");
        await Assert.That(body).Contains("\"tool_call_id\":\"c1\"");
    }

    [Test]
    public async Task StreamResponseAsync_AssistantMessageWithoutToolCalls_OmitsToolCallsField()
    {
        var handler = new FixtureHttpHandler("Fixtures/DeepSeekBasicChat.sse");
        var provider = CreateProvider(handler);

        var assistant = new AssistantMessage
        {
            Content = [new TextBlock("All done.")],
            StopReason = StopReasons.Stop,
        };

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "deepseek-v4-flash",
            system: "...",
            messages: [
                new UserMessage { Content = "Hi" },
                assistant,
            ],
            tools: []));

        await Assert.That(handler.LastRequestBody!.Contains("\"tool_calls\"")).IsFalse();
    }

    private static async Task<List<ProviderEvent>> CollectEvents(
        IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var ev in source) list.Add(ev);
        return list;
    }
}
