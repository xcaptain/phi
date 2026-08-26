using System.Text.Json.Nodes;
using Phi.Agent;

namespace Phi.Provider.Tests;

public class OpenAICompatibleProviderToolCallTests
{
    private static readonly string[] ExpectedToolNames = ["bash", "read"];

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
        var textUpdates = events.OfType<TextDeltaEvent>().ToList();
        var toolUpdates = events.OfType<ToolCallEvent>().ToList();
        await Assert.That(textUpdates.Count).IsEqualTo(1);
        await Assert.That(toolUpdates.Count).IsEqualTo(2);
        await Assert.That(events.OfType<AssistantDoneEvent>().Count).IsEqualTo(1);

        // First text delta
        await Assert.That(textUpdates[0].Delta).IsEqualTo("Let me check.");

        // First tool call (index 0 = bash)
        await Assert.That(toolUpdates[0].ToolCall.Id).IsEqualTo("call_bash");
        await Assert.That(toolUpdates[0].ToolCall.Name).IsEqualTo("bash");
        await Assert.That(toolUpdates[0].ToolCall.Arguments["command"]!.GetValue<string>()).IsEqualTo("ls -la");

        // Second tool call (index 1 = read)
        await Assert.That(toolUpdates[1].ToolCall.Id).IsEqualTo("call_read");
        await Assert.That(toolUpdates[1].ToolCall.Name).IsEqualTo("read");
        await Assert.That(toolUpdates[1].ToolCall.Arguments["path"]!.GetValue<string>()).IsEqualTo("/tmp/x");
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

        var end = events.OfType<AssistantDoneEvent>().Single();
        await Assert.That(end.FinishReason).IsEqualTo(StopReasons.ToolUse);
        // The streamed content (text + 2 tool calls) arrives as granular events.
        await Assert.That(events.OfType<TextDeltaEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<ToolCallEvent>().Count()).IsEqualTo(2);
        await Assert.That(events.OfType<TextDeltaEvent>().Single().Delta)
            .IsEqualTo("Let me check.");
        await Assert.That(events.OfType<ToolCallEvent>().Select(t => t.ToolCall.Name))
            .IsEquivalentTo(ExpectedToolNames);
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
