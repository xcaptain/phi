using System.Text.Json.Nodes;

namespace PhiAgent.Tests;

public class LoopTests
{
    private static ToolExecutor NeverCalledExecutor =>
        (_, _, _, _) => throw new InvalidOperationException("Tool should not be called");

    private static AssistantMessage FinalMessage(string text) =>
        new()
        {
            Api = "test",
            Provider = "fake",
            Model = "test",
            Content = [new TextBlock(text)],
            StopReason = StopReasons.Stop,
        };

    private static AssistantMessage ToolUseMessage(ToolCall call, string prefix = "") =>
        new()
        {
            Api = "test",
            Provider = "fake",
            Model = "test",
            Content = [new TextBlock(prefix), call],
            StopReason = StopReasons.ToolUse,
        };

    [Test]
    public async Task RunTurnAsync_NoToolCalls_EmitsTextDeltasAndTurnEnd()
    {
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Hello"),
                new ProviderTextDeltaEvent(" world"),
                new ProviderResponseEndEvent(FinalMessage("Hello world"), StopReasons.Stop),
            },
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "Hi" } };

        var events = new List<HarnessEvent>();
        await foreach (var ev in Loop.RunTurnAsync(
            fake, "test", "", messages, [], NeverCalledExecutor))
        {
            events.Add(ev);
        }

        await Assert.That(events.OfType<AssistantTextDeltaEvent>().Count()).IsEqualTo(2);
        await Assert.That(events.OfType<ToolExecutionStartEvent>().Count()).IsEqualTo(0);
        await Assert.That(events.OfType<TurnEndEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.Last()).IsTypeOf<TurnEndEvent>();
        await Assert.That(((TurnEndEvent)events.Last()).FinalMessage.Text).IsEqualTo("Hello world");

        await Assert.That(messages.Count).IsEqualTo(2);
        await Assert.That(messages[0]).IsTypeOf<UserMessage>();
        await Assert.That(messages[1]).IsTypeOf<AssistantMessage>();
    }

    [Test]
    public async Task RunTurnAsync_ToolCall_ExecutesAndLoopsForFinalAnswer()
    {
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{"command":"ls"}""")!.AsObject(),
        };

        var fake = new FakePhiProvider(
        [
            [
                new ProviderTextDeltaEvent("Let me check"),
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall, "Let me check"), StopReasons.ToolUse),
            ],
            [
                new ProviderTextDeltaEvent("3 files"),
                new ProviderResponseEndEvent(FinalMessage("3 files"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "list files" } };

        var executed = new List<string>();
        var events = new List<HarnessEvent>();
        await foreach (var ev in Loop.RunTurnAsync(
            fake, "test", "", messages, [],
            async (_, _, args, _) =>
            {
                var cmd = args["command"]?.GetValue<string>() ?? "";
                executed.Add(cmd);
                return new ToolResult([new TextBlock($"output of {cmd}")]);
            }))
        {
            events.Add(ev);
        }

        await Assert.That(executed).IsEquivalentTo(["ls"]);

        await Assert.That(events.OfType<AssistantToolCallEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<ToolExecutionStartEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<ToolExecutionEndEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<TurnEndEvent>().Count()).IsEqualTo(1);

        // Event order check
        var kinds = events.Select(e => e.GetType().Name).ToList();
        await Assert.That(kinds).IsEquivalentTo(
        [
            "AssistantTextDeltaEvent",
            "AssistantToolCallEvent",
            "ToolExecutionStartEvent",
            "ToolExecutionEndEvent",
            "AssistantTextDeltaEvent",
            "TurnEndEvent",
        ]);

        // Messages: user + assistant(tool call) + tool_result + assistant(final) = 4
        await Assert.That(messages.Count).IsEqualTo(4);
        await Assert.That(messages[2]).IsTypeOf<ToolResultMessage>();
        await Assert.That(((ToolResultMessage)messages[2]).Text).IsEqualTo("output of ls");
    }

    [Test]
    public async Task RunTurnAsync_NoFinalResponse_ThrowsWithProviderError()
    {
        var fake = new FakePhiProvider(
        [
            [
                new ProviderErrorEvent("HTTP 401: invalid key"),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "hi" } };

        var ex = await Assert.That(async () =>
        {
            await foreach (var _ in Loop.RunTurnAsync(
                fake, "test", "", messages, [], NeverCalledExecutor)) { }
        }).Throws<InvalidOperationException>();

        await Assert.That(ex!.Message).Contains("HTTP 401");
    }

    [Test]
    public async Task RunTurnAsync_ToolThrows_LoopCatchesAndSurfacesAsIsError()
    {
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{"command":"ls"}""")!.AsObject(),
        };

        var fake = new FakePhiProvider(
        [
            [
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ],
            [
                new ProviderResponseEndEvent(FinalMessage("done"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };

        static Task<ToolResult> Boom(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => throw new InvalidOperationException("kaboom");

        var events = new List<HarnessEvent>();
        await foreach (var ev in Loop.RunTurnAsync(
            fake, "test", "", messages, [], Boom))
        {
            events.Add(ev);
        }

        var result = (ToolResultMessage)messages[2];
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("kaboom");

        await Assert.That(messages.Count).IsEqualTo(4);
        await Assert.That(messages.Last()).IsTypeOf<AssistantMessage>();
        await Assert.That(((TurnEndEvent)events.Last()).FinalMessage.Text).IsEqualTo("done");
    }

    [Test]
    public async Task RunTurnAsync_UnknownTool_ReturnsErrorResultWithoutThrowing()
    {
        var toolCall = new ToolCall("c1", "mystery")
        {
            Arguments = JsonNode.Parse("""{}""")!.AsObject(),
        };

        var fake = new FakePhiProvider(
        [
            [
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ],
            [
                new ProviderResponseEndEvent(FinalMessage("ok"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };

        static Task<ToolResult> Missing(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("Unknown tool: mystery")], IsError: true));

        await foreach (var _ in Loop.RunTurnAsync(
            fake, "test", "", messages, [], Missing)) { }

        var result = (ToolResultMessage)messages[2];
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).IsEqualTo("Unknown tool: mystery");
    }

    [Test]
    public async Task RunTurnAsync_PreservesToolResultDetails()
    {
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{}""")!.AsObject(),
        };

        var fake = new FakePhiProvider(
        [
            [
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ],
            [
                new ProviderResponseEndEvent(FinalMessage("done"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };
        var details = JsonNode.Parse("""{"path":"/tmp/x","lines":42}""")!;

        Task<ToolResult> WithDetails(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("ok")], Details: details));

        await foreach (var _ in Loop.RunTurnAsync(
            fake, "test", "", messages, [], WithDetails)) { }

        var result = (ToolResultMessage)messages[2];
        await Assert.That(result.Details).IsNotNull();
        await Assert.That(result.Details!["path"]!.GetValue<string>()).IsEqualTo("/tmp/x");
        await Assert.That(result.Details!["lines"]!.GetValue<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task RunTurnAsync_MaxTurnsExceeded_StopsWithErrorMessage()
    {
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{}""")!.AsObject(),
        };

        var fake = new FakePhiProvider([.. Enumerable.Range(0, 5).Select(_ =>
            (IEnumerable<ProviderEvent>)
            [
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ])]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };

        static Task<ToolResult> LoopForever(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("ok")]));

        var events = new List<HarnessEvent>();
        await foreach (var ev in Loop.RunTurnAsync(
            fake, "test", "", messages, [], LoopForever, maxTurns: 2))
        {
            events.Add(ev);
        }

        await Assert.That(events.OfType<TurnEndEvent>().Count()).IsEqualTo(1);
        var final = ((TurnEndEvent)events.Last()).FinalMessage;
        await Assert.That(final.StopReason).IsEqualTo(StopReasons.Error);
        await Assert.That(final.Text).Contains("max_turns=2");

        // messages: user + assistant(tool call) + tool_result + assistant(tool call) + tool_result + assistant(error)
        await Assert.That(messages.Count).IsEqualTo(6);
        await Assert.That(messages.Last()).IsTypeOf<AssistantMessage>();
    }

    [Test]
    public async Task RunTurnAsync_Cancellation_Propagates()
    {
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{}""")!.AsObject(),
        };

        var fake = new FakePhiProvider(
        [
            [
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        static Task<ToolResult> Cancellable(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => Task.FromCanceled<ToolResult>(cancellationToken);

        await Assert.That(async () =>
        {
            await foreach (var _ in Loop.RunTurnAsync(
                fake, "test", "", messages, [], Cancellable, cancellationToken: cts.Token)) { }
        }).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RunTurnAsync_ThinkingLifecycle_IsTranslatedToStartDeltaEndEvents()
    {
        var consolidated = new ThinkingBlock("reasoning about...so my answer is 42.")
        {
            ThinkingSignature = "sig-xyz",
        };
        var final = new AssistantMessage
        {
            Api = "test",
            Provider = "fake",
            Model = "test",
            Content = [consolidated, new TextBlock("The answer is 42.")],
            StopReason = StopReasons.Stop,
        };

        var fake = new FakePhiProvider(
        [
            [
                new ProviderThinkingStartEvent(),
                new ProviderThinkingDeltaEvent("reasoning about..."),
                new ProviderThinkingDeltaEvent("so my answer is 42."),
                new ProviderThinkingEndEvent(consolidated),
                new ProviderTextDeltaEvent("The answer is 42."),
                new ProviderResponseEndEvent(final, StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "Hi" } };

        var events = new List<HarnessEvent>();
        await foreach (var ev in Loop.RunTurnAsync(
            fake, "test", "", messages, [], NeverCalledExecutor))
        {
            events.Add(ev);
        }

        // Start appears exactly once, before any delta.
        var starts = events.OfType<AssistantThinkingStartEvent>().ToList();
        await Assert.That(starts.Count).IsEqualTo(1);
        var firstStartIdx = events.IndexOf(starts[0]);

        var deltas = events.OfType<AssistantThinkingDeltaEvent>().ToList();
        await Assert.That(deltas.Count).IsEqualTo(2);
        await Assert.That(deltas[0].Delta).IsEqualTo("reasoning about...");
        await Assert.That(deltas[1].Delta).IsEqualTo("so my answer is 42.");
        await Assert.That(events.IndexOf(deltas[0])).IsGreaterThan(firstStartIdx);

        // End carries the consolidated block with signature.
        var ends = events.OfType<AssistantThinkingEndEvent>().ToList();
        await Assert.That(ends.Count).IsEqualTo(1);
        await Assert.That(ends[0].Block.Thinking)
            .IsEqualTo("reasoning about...so my answer is 42.");
        await Assert.That(ends[0].Block.ThinkingSignature).IsEqualTo("sig-xyz");
        await Assert.That(events.IndexOf(ends[0])).IsGreaterThan(events.IndexOf(deltas[1]));

        // Final message still carries the ThinkingBlock.
        var turnEnd = events.OfType<TurnEndEvent>().Single();
        await Assert.That(turnEnd.FinalMessage.Content.OfType<ThinkingBlock>().Single())
            .IsEquivalentTo(consolidated);
    }
}