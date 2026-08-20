using System.Text.Json.Nodes;
using Phi.Agent;

namespace Phi.Provider.Tests;

public class OpenAICompatibleProviderToolCallTests
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
    public async Task StreamResponseAsync_ToolCalls_AccumulatesFragmentsAndEmitsEvents()
    {
        var handler = new FixtureHttpHandler("Fixtures/DeepSeekToolCall.sse");
        var provider = CreateProvider(handler);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "deepseek-v4-flash",
            system: "You are helpful",
            messages: [new UserMessage { Content = "Check the system" }],
            tools: [
                new StubTool("bash", "Run a shell command", new JsonObject
                {
                    ["type"] = "object",
                }),
                new StubTool("read", "Read a file", new JsonObject
                {
                    ["type"] = "object",
                }),
            ]))
        {
            events.Add(ev);
        }

        // 1 text delta + 2 tool call events + 1 response end = 4 events
        await Assert.That(events.OfType<ProviderTextDeltaEvent>().Count()).IsEqualTo(1);
        var toolCallEvents = events.OfType<ProviderToolCallEvent>().ToList();
        await Assert.That(toolCallEvents.Count).IsEqualTo(2);
        await Assert.That(events.OfType<ProviderResponseEndEvent>().Count).IsEqualTo(1);

        // First text delta
        var firstText = events.OfType<ProviderTextDeltaEvent>().Single();
        await Assert.That(firstText.Delta).IsEqualTo("Let me check.");

        // First tool call (index 0 = bash)
        await Assert.That(toolCallEvents[0].ToolCall.Id).IsEqualTo("call_bash");
        await Assert.That(toolCallEvents[0].ToolCall.Name).IsEqualTo("bash");
        await Assert.That(toolCallEvents[0].ToolCall.Arguments["command"]!.GetValue<string>()).IsEqualTo("ls -la");

        // Second tool call (index 1 = read)
        await Assert.That(toolCallEvents[1].ToolCall.Id).IsEqualTo("call_read");
        await Assert.That(toolCallEvents[1].ToolCall.Name).IsEqualTo("read");
        await Assert.That(toolCallEvents[1].ToolCall.Arguments["path"]!.GetValue<string>()).IsEqualTo("/tmp/x");
    }

    [Test]
    public async Task StreamResponseAsync_ToolCalls_FinalAssistantMessageContainsTextAndToolCallsInOrder()
    {
        var handler = new FixtureHttpHandler("Fixtures/DeepSeekToolCall.sse");
        var provider = CreateProvider(handler);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "deepseek-v4-flash",
            system: "You are helpful",
            messages: [new UserMessage { Content = "Check the system" }],
            tools: [
                new StubTool("bash", "Run a shell command", []),
                new StubTool("read", "Read a file", []),
            ]))
        {
            events.Add(ev);
        }

        var end = events.OfType<ProviderResponseEndEvent>().Single();
        await Assert.That(end.FinishReason).IsEqualTo(StopReasons.ToolUse);
        await Assert.That(end.Message.Content.Count).IsEqualTo(3);

        // Order: TextBlock first, then ToolCalls in stream index order
        var textBlock = end.Message.Content[0];
        await Assert.That(textBlock).IsTypeOf<TextBlock>();
        await Assert.That(((TextBlock)textBlock).Text).IsEqualTo("Let me check.");

        var firstTool = end.Message.Content[1];
        await Assert.That(firstTool).IsTypeOf<ToolCall>();
        await Assert.That(((ToolCall)firstTool).Name).IsEqualTo("bash");

        var secondTool = end.Message.Content[2];
        await Assert.That(secondTool).IsTypeOf<ToolCall>();
        await Assert.That(((ToolCall)secondTool).Name).IsEqualTo("read");

        // AssistantMessage computed properties still work
        await Assert.That(end.Message.Text).IsEqualTo("Let me check.");
        await Assert.That(end.Message.ToolCalls.Count).IsEqualTo(2);
    }

    [Test]
    public async Task StreamResponseAsync_ToolCalls_IncludesToolsArrayInRequestBody()
    {
        var handler = new FixtureHttpHandler("Fixtures/DeepSeekToolCall.sse");
        var provider = CreateProvider(handler);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "deepseek-v4-flash",
            system: "You are helpful",
            messages: [new UserMessage { Content = "Check" }],
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

        await Assert.That(handler.LastRequestBody).IsNotNull();
        await Assert.That(handler.LastRequestBody!).Contains("\"tools\":[");
        await Assert.That(handler.LastRequestBody!).Contains("\"type\":\"function\"");
        await Assert.That(handler.LastRequestBody!).Contains("\"name\":\"bash\"");
        await Assert.That(handler.LastRequestBody!).Contains("\"description\":\"Run a shell command\"");
        await Assert.That(handler.LastRequestBody!).Contains("\"parameters\":");
    }

    private static async Task<List<ProviderEvent>> CollectEvents(
        IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var ev in source) list.Add(ev);
        return list;
    }
}
