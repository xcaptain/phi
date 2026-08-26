using System.Text.Json.Nodes;
using Phi.Agent;

namespace Phi.Provider.Tests;

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
        var textUpdates = events.OfType<TextDeltaEvent>().ToList();
        var toolUpdates = events.OfType<ToolCallEvent>().ToList();
        await Assert.That(textUpdates.Count).IsEqualTo(1);
        await Assert.That(textUpdates[0].Delta).IsEqualTo("I'll check that.");

        // Exactly one tool call.
        await Assert.That(toolUpdates.Count).IsEqualTo(1);
        await Assert.That(toolUpdates[0].ToolCall.Id).IsEqualTo("toolu_bash");
        await Assert.That(toolUpdates[0].ToolCall.Name).IsEqualTo("bash");
        await Assert.That(toolUpdates[0].ToolCall.Arguments["command"]!.GetValue<string>())
            .IsEqualTo("ls -la");

        // Response ends with tool_use stop reason.
        var end = events.OfType<AssistantDoneEvent>().Single();
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

        // The provider doesn't accumulate content into the terminal's
        // Message.Content (the loop's canonicalizer owns the partial).
        // Here we just verify the granular events arrived correctly —
        // building the final Content is the loop's job.
        var textDelta = events.OfType<TextDeltaEvent>().Single();
        await Assert.That(textDelta.Delta).IsEqualTo("I'll check that.");

        var thinkingDelta = events.OfType<ThinkingDeltaEvent>().Single();
        await Assert.That(thinkingDelta.Delta).IsEqualTo("Let me check the system.");

        var toolCallEvent = events.OfType<ToolCallEvent>().Single();
        await Assert.That(toolCallEvent.ToolCall.Id).IsEqualTo("toolu_bash");
        await Assert.That(toolCallEvent.ToolCall.Name).IsEqualTo("bash");
        await Assert.That(toolCallEvent.ToolCall.Arguments["command"]!.GetValue<string>())
            .IsEqualTo("ls -la");

        var end = events.OfType<AssistantDoneEvent>().Single();
        await Assert.That(end.FinishReason).IsEqualTo(StopReasons.ToolUse);
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

        var end = events.OfType<AssistantDoneEvent>().Single();
        await Assert.That(end.Message.StopReason).IsEqualTo(StopReasons.ToolUse);
    }

    [Test]
    public async Task StreamResponseAsync_ThinkingBlockLifecycle_EmitsDeltasAndEnd()
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

        // Lifecycle: deltas → end (blocks open lazily on the first delta;
        // there is no separate ThinkingStartEvent in the protocol).
        var deltas = events.OfType<ThinkingDeltaEvent>().ToList();
        var ends = events.OfType<ThinkingEndEvent>().ToList();

        await Assert.That(deltas.Count).IsEqualTo(1);
        await Assert.That(deltas[0].Delta).IsEqualTo("Let me check the system.");
        await Assert.That(ends.Count).IsEqualTo(1);

        // The end event carries the signature (collected across signature_delta
        // events by the canonicalizer). The Thinking text itself is
        // accumulated from the deltas in the streamed partial — verify it
        // directly from the granular deltas rather than the event payload.
        await Assert.That(ends[0].Block.ThinkingSignature).IsEqualTo("sig_abc123");
        await Assert.That(string.Concat(deltas.Select(d => d.Delta)))
            .IsEqualTo("Let me check the system.");

        // Order: first delta < end.
        var deltaIdx = events.FindLastIndex(e => e is ThinkingDeltaEvent);
        var endIdx = events.FindIndex(e => e is ThinkingEndEvent);
        await Assert.That(deltaIdx).IsLessThan(endIdx);
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
        var thinking = events.OfType<ThinkingDeltaEvent>().ToList();
        await Assert.That(thinking.Count).IsEqualTo(3);
        await Assert.That(thinking[0].Delta).IsEqualTo("Step 1: ");
        await Assert.That(thinking[1].Delta).IsEqualTo("check the ");
        await Assert.That(thinking[2].Delta).IsEqualTo("system.");

        // End carries the signature (collected from signature_delta). The
        // Thinking text itself is reconstructed from the streamed deltas in
        // the partial — verify directly from the deltas rather than the
        // event payload.
        var end = events.OfType<ThinkingEndEvent>().Single();
        await Assert.That(end.Block.ThinkingSignature).IsEqualTo("sig-multi");
        await Assert.That(string.Concat(thinking.Select(d => d.Delta)))
            .IsEqualTo("Step 1: check the system.");

        // The provider's terminal Message has empty Content (the loop's
        // canonicalizer owns the partial). Verify the granular events are
        // correct and that the partial reconstruction in the loop would
        // produce the expected block.
        var deltas = events.OfType<ThinkingDeltaEvent>().ToList();
        var reconstructed = string.Concat(deltas.Select(d => d.Delta));
        await Assert.That(reconstructed).IsEqualTo("Step 1: check the system.");

        var endEv = events.OfType<ThinkingEndEvent>().Single();
        // The thinking text is accumulated via the streamed deltas in the
        // partial. The end event itself only carries the signature (the
        // canonicalizer owns the partial). Verify both.
        await Assert.That(endEv.Block.ThinkingSignature).IsEqualTo("sig-multi");
        await Assert.That(string.Concat(deltas.Select(d => d.Delta)))
            .IsEqualTo("Step 1: check the system.");

        // Order: thinking lifecycle happens before the text delta.
        var firstTextDeltaIdx = events.FindIndex(e => e is TextDeltaEvent);
        var lastThinkingDeltaIdx = events.FindLastIndex(e => e is ThinkingDeltaEvent);
        await Assert.That(lastThinkingDeltaIdx).IsLessThan(firstTextDeltaIdx);
    }

    [Test]
    public async Task StreamResponseAsync_TextBlocksDoNotEmitThinkingEvents()
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

        await Assert.That(events.OfType<ThinkingDeltaEvent>()).IsEmpty();
        await Assert.That(events.OfType<ThinkingEndEvent>()).IsEmpty();
    }

    [Test]
    public async Task StreamResponseAsync_ThinkingBlockWithoutSignature_StillEmitsEnd()
    {
        // Regression: the end event must fire for EVERY thinking block that
        // streamed a delta, not just the ones that carried signature_delta
        // fragments (models without extended-thinking signatures, or
        // gateways that strip them, still close their thinking blocks).
        var handler = new InlineSseHandler("""
            event: message_start
            data: {"type":"message_start","message":{"id":"m","model":"claude-sonnet-4-5","usage":{"input_tokens":1,"output_tokens":1,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"no signature here"}}

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

        var end = events.OfType<ThinkingEndEvent>().Single();
        await Assert.That(end.Block.ThinkingSignature).IsNull();
        // The end event lands after the delta, before the text block starts.
        var deltaIdx = events.FindIndex(e => e is ThinkingDeltaEvent);
        var endIdx = events.FindIndex(e => e is ThinkingEndEvent);
        var textIdx = events.FindIndex(e => e is TextDeltaEvent);
        await Assert.That(deltaIdx).IsLessThan(endIdx);
        await Assert.That(endIdx).IsLessThan(textIdx);
    }

    private static async Task<List<ProviderEvent>> CollectEvents(
        IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var ev in source) list.Add(ev);
        return list;
    }
}
