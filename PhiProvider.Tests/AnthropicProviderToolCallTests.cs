using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiProvider.Tests;

public class AnthropicProviderToolCallTests
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
    public async Task StreamResponseAsync_ToolCalls_AccumulatesFragmentsAndEmitsEvents()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicThinkingToolCall.sse");
        var provider = CreateProvider(handler);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "You are helpful",
            messages: [new UserMessage { Content = "Check the system" }],
            tools: [
                new StubTool("bash", "Run a shell command", new JsonObject { ["type"] = "object" }),
            ]))
        {
            events.Add(ev);
        }

        // Text delta (the "I'll check that." text block).
        await Assert.That(events.OfType<ProviderTextDeltaEvent>().Count()).IsEqualTo(1);
        var firstText = events.OfType<ProviderTextDeltaEvent>().Single();
        await Assert.That(firstText.Delta).IsEqualTo("I'll check that.");

        // Exactly one tool call.
        var toolCallEvents = events.OfType<ProviderToolCallEvent>().ToList();
        await Assert.That(toolCallEvents.Count).IsEqualTo(1);
        await Assert.That(toolCallEvents[0].ToolCall.Id).IsEqualTo("toolu_bash");
        await Assert.That(toolCallEvents[0].ToolCall.Name).IsEqualTo("bash");
        await Assert.That(toolCallEvents[0].ToolCall.Arguments["command"]!.GetValue<string>())
            .IsEqualTo("ls -la");

        // Response ends with tool_use stop reason.
        var end = events.OfType<ProviderResponseEndEvent>().Single();
        await Assert.That(end.FinishReason).IsEqualTo(StopReasons.ToolUse);
    }

    [Test]
    public async Task StreamResponseAsync_ToolCalls_FinalAssistantMessageHasThinkingAndToolCallInOrder()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicThinkingToolCall.sse");
        var provider = CreateProvider(handler);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "You are helpful",
            messages: [new UserMessage { Content = "Check the system" }],
            tools: [
                new StubTool("bash", "Run a shell command", new JsonObject { ["type"] = "object" }),
            ]))
        {
            events.Add(ev);
        }

        var end = events.OfType<ProviderResponseEndEvent>().Single();
        var content = end.Message.Content;

        // Order: ThinkingBlock → TextBlock → ToolCall
        await Assert.That(content.Count).IsEqualTo(3);
        await Assert.That(content[0]).IsTypeOf<ThinkingBlock>();
        await Assert.That(content[1]).IsTypeOf<TextBlock>();
        await Assert.That(content[2]).IsTypeOf<ToolCall>();

        var thinking = (ThinkingBlock)content[0];
        await Assert.That(thinking.Thinking).IsEqualTo("Let me check the system.");
        await Assert.That(thinking.ThinkingSignature).IsEqualTo("sig_abc123");

        var text = (TextBlock)content[1];
        await Assert.That(text.Text).IsEqualTo("I'll check that.");

        var toolCall = (ToolCall)content[2];
        await Assert.That(toolCall.Name).IsEqualTo("bash");
        await Assert.That(toolCall.Arguments["command"]!.GetValue<string>())
            .IsEqualTo("ls -la");
    }

    [Test]
    public async Task StreamResponseAsync_ToolCalls_ToolsArrayIsSentWithInputSchema()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicThinkingToolCall.sse");
        var provider = CreateProvider(handler);

        _ = await CollectEvents(provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
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

        var body = handler.LastRequestBody!;
        await Assert.That(body).Contains("\"tools\":[");
        await Assert.That(body).Contains("\"name\":\"bash\"");
        await Assert.That(body).Contains("\"description\":\"Run a shell command\"");
        await Assert.That(body).Contains("\"input_schema\":");
        await Assert.That(body).DoesNotContain("\"parameters\":");
    }

    [Test]
    public async Task StreamResponseAsync_ToolCalls_StopReasonToolUseMapsToToolUse()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicThinkingToolCall.sse");
        var provider = CreateProvider(handler);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [new UserMessage { Content = "Check" }],
            tools: [
                new StubTool("bash", "Run", new JsonObject { ["type"] = "object" }),
            ]))
        {
            events.Add(ev);
        }

        var end = events.OfType<ProviderResponseEndEvent>().Single();
        await Assert.That(end.Message.StopReason).IsEqualTo(StopReasons.ToolUse);
    }

    [Test]
    public async Task StreamResponseAsync_ThinkingBlockLifecycle_EmitsStartDeltasAndEnd()
    {
        var handler = new FixtureHttpHandler("Fixtures/AnthropicThinkingToolCall.sse");
        var provider = CreateProvider(handler);

        var events = new List<ProviderEvent>();
        await foreach (var ev in provider.StreamResponseAsync(
            model: "claude-sonnet-4-5",
            system: "",
            messages: [new UserMessage { Content = "Check" }],
            tools: [
                new StubTool("bash", "Run", new JsonObject { ["type"] = "object" }),
            ]))
        {
            events.Add(ev);
        }

        // Lifecycle: start → 1 delta → end, in that order.
        var starts = events.OfType<ProviderThinkingStartEvent>().ToList();
        var deltas = events.OfType<ProviderThinkingDeltaEvent>().ToList();
        var ends = events.OfType<ProviderThinkingEndEvent>().ToList();

        await Assert.That(starts.Count).IsEqualTo(1);
        await Assert.That(deltas.Count).IsEqualTo(1);
        await Assert.That(deltas[0].Delta).IsEqualTo("Let me check the system.");
        await Assert.That(ends.Count).IsEqualTo(1);

        // End carries the consolidated block with the signature collected
        // from signature_delta.
        await Assert.That(ends[0].Block.Thinking).IsEqualTo("Let me check the system.");
        await Assert.That(ends[0].Block.ThinkingSignature).IsEqualTo("sig_abc123");

        // Order: start < delta < end.
        await Assert.That(events.IndexOf(starts[0])).IsLessThan(events.IndexOf(deltas[0]));
        await Assert.That(events.IndexOf(deltas[0])).IsLessThan(events.IndexOf(ends[0]));
    }

    [Test]
    public async Task StreamResponseAsync_MultipleThinkingDeltas_AccumulateAndStream()
    {
        var handler = new InlineSseHandler("""
            event: message_start
            data: {"type":"message_start","message":{"id":"m","model":"claude-sonnet-4-5","usage":{"input_tokens":1,"output_tokens":1,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Step 1: "}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"check the "}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"system."}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-multi"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: content_block_start
            data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Done."}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":1}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}

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
            messages: [new UserMessage { Content = "go" }],
            tools: []))
        {
            events.Add(ev);
        }

        // Three thinking deltas stream individually, in order.
        var thinking = events.OfType<ProviderThinkingDeltaEvent>().ToList();
        await Assert.That(thinking.Count).IsEqualTo(3);
        await Assert.That(thinking[0].Delta).IsEqualTo("Step 1: ");
        await Assert.That(thinking[1].Delta).IsEqualTo("check the ");
        await Assert.That(thinking[2].Delta).IsEqualTo("system.");

        // End consolidates them AND keeps the signature.
        var end = events.OfType<ProviderThinkingEndEvent>().Single();
        await Assert.That(end.Block.Thinking).IsEqualTo("Step 1: check the system.");
        await Assert.That(end.Block.ThinkingSignature).IsEqualTo("sig-multi");

        // Final AssistantMessage also carries the same consolidated block.
        var responseEnd = events.OfType<ProviderResponseEndEvent>().Single();
        var block = responseEnd.Message.Content.OfType<ThinkingBlock>().Single();
        await Assert.That(block.Thinking).IsEqualTo("Step 1: check the system.");
        await Assert.That(block.ThinkingSignature).IsEqualTo("sig-multi");

        // Order: thinking lifecycle happens before the text delta.
        var firstTextDeltaIdx = events.FindIndex(e => e is ProviderTextDeltaEvent);
        var lastThinkingDeltaIdx = events.FindLastIndex(e => e is ProviderThinkingDeltaEvent);
        await Assert.That(lastThinkingDeltaIdx).IsLessThan(firstTextDeltaIdx);
    }

    [Test]
    public async Task StreamResponseAsync_TextBlocksDoNotEmitThinkingStartOrEnd()
    {
        // Sanity check: only thinking blocks trigger thinking lifecycle events.
        // Text blocks must stay quiet on that channel.
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

        await Assert.That(events.OfType<ProviderThinkingStartEvent>().Count()).IsEqualTo(0);
        await Assert.That(events.OfType<ProviderThinkingDeltaEvent>().Count()).IsEqualTo(0);
        await Assert.That(events.OfType<ProviderThinkingEndEvent>().Count()).IsEqualTo(0);
    }

    private static async Task<List<ProviderEvent>> CollectEvents(
        IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var ev in source) list.Add(ev);
        return list;
    }
}
