using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiProvider.Tests;

public class AnthropicProviderMessageConversionTests
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
    public async Task StreamResponseAsync_PostsToMessagesEndpointWithSystemAsTopLevelField()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var provider = CreateProvider(handler);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "you are helpful",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []));

        await Assert.That(handler.LastRequestUri)
            .IsEqualTo("https://api.anthropic.com/v1/messages");
        var body = handler.LastRequestBody!;

        // system is a top-level field, NOT a message with role=system
        await Assert.That(body).Contains("\"system\":\"you are helpful\"");
        await Assert.That(body).DoesNotContain("\"role\":\"system\"");

        await Assert.That(body).Contains("\"model\":\"claude-sonnet-4-5\"");
        await Assert.That(body).Contains("\"stream\":true");
        await Assert.That(body).Contains("\"max_tokens\":4096");

        await Assert.That(body).Contains("\"role\":\"user\"");
        await Assert.That(body).Contains("\"content\":\"Hi\"");
    }

    [Test]
    public async Task StreamResponseAsync_EmptySystem_OmitsSystemField()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var provider = CreateProvider(handler);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [new UserMessage { Content = "Hi" }],
            tools: []));

        // No system field when empty.
        await Assert.That(handler.LastRequestBody!.Contains("\"system\"")).IsFalse();
    }

    [Test]
    public async Task StreamResponseAsync_ToolUsesInputSchemaAndNoTypeWrapper()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var provider = CreateProvider(handler);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [new UserMessage { Content = "Hi" }],
            tools: [
                new StubTool("bash", "Run a shell command", new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["command"] = new JsonObject { ["type"] = "string" },
                    },
                }),
            ]));

        var body = handler.LastRequestBody!;

        // Anthropic uses input_schema, not parameters.
        await Assert.That(body).Contains("\"input_schema\":");
        await Assert.That(body).DoesNotContain("\"parameters\":");
        // No {"type":"function", "function":{...}} wrapper.
        await Assert.That(body).DoesNotContain("\"type\":\"function\"");
        // Tool is a flat object at the top of the array.
        await Assert.That(body).Contains("\"name\":\"bash\"");
        await Assert.That(body).Contains("\"description\":\"Run a shell command\"");
    }

    [Test]
    public async Task StreamResponseAsync_AssistantMessageWithToolCalls_SerializesToolUseBlocks()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var provider = CreateProvider(handler);

        var assistant = new AssistantMessage
        {
            Content = [
                new ToolCall("toolu_01", "bash")
                {
                    Arguments = JsonNode.Parse("""{"command":"ls"}""")!.AsObject(),
                },
            ],
            StopReason = StopReasons.ToolUse,
        };

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [
                new UserMessage { Content = "Check system" },
                assistant,
                new ToolResultMessage
                {
                    ToolCallId = "toolu_01",
                    ToolName = "bash",
                    Content = [new TextBlock("ok output")],
                },
            ],
            tools: []));

        var body = handler.LastRequestBody!;

        // Assistant message uses tool_use blocks, NOT tool_calls array.
        await Assert.That(body).Contains("\"role\":\"assistant\"");
        await Assert.That(body).Contains("\"type\":\"tool_use\"");
        await Assert.That(body).Contains("\"id\":\"toolu_01\"");
        await Assert.That(body).Contains("\"name\":\"bash\"");
        await Assert.That(body).Contains("\"input\":");
        await Assert.That(body).DoesNotContain("\"tool_calls\":");

        // Tool result is a user message with an embedded tool_result block.
        await Assert.That(body).Contains("\"role\":\"user\"");
        await Assert.That(body).Contains("\"type\":\"tool_result\"");
        await Assert.That(body).Contains("\"tool_use_id\":\"toolu_01\"");
        await Assert.That(body).Contains("\"is_error\":false");
        await Assert.That(body).Contains("ok output");
    }

    [Test]
    public async Task StreamResponseAsync_ToolResultWithIsError_EmitsIsErrorTrue()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicBasicChat.sse");
        var provider = CreateProvider(handler);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [
                new UserMessage { Content = "Run it" },
                new AssistantMessage
                {
                    Content = [new ToolCall("toolu_x", "bash")],
                    StopReason = StopReasons.ToolUse,
                },
                new ToolResultMessage
                {
                    ToolCallId = "toolu_x",
                    ToolName = "bash",
                    Content = [new TextBlock("command failed")],
                    IsError = true,
                },
            ],
            tools: []));

        await Assert.That(handler.LastRequestBody!).Contains("\"is_error\":true");
    }

    private static async Task<List<ProviderEvent>> CollectEvents(
        IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var ev in source) list.Add(ev);
        return list;
    }
}