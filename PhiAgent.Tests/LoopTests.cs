using System.Text.Json.Nodes;
using PhiAgent;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

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
    public async Task RunAgentAsync_NoToolCalls_EmitsTextDeltasAndTurnEnd()
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
        await foreach (var ev in Loop.RunAgentAsync(
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
    public async Task RunAgentAsync_ToolCall_ExecutesAndLoopsForFinalAnswer()
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
        await foreach (var ev in Loop.RunAgentAsync(
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

        var kinds = events.Select(e => e.GetType().Name).ToList();
        await Assert.That(kinds).IsEquivalentTo(
        [
            "TurnStartEvent",
            "AssistantTextDeltaEvent",
            "AssistantToolCallEvent",
            "ToolExecutionStartEvent",
            "ToolExecutionEndEvent",
            "TurnStartEvent",
            "AssistantTextDeltaEvent",
            "TurnEndEvent",
        ]);

        await Assert.That(messages.Count).IsEqualTo(4);
        await Assert.That(messages[2]).IsTypeOf<ToolResultMessage>();
        await Assert.That(((ToolResultMessage)messages[2]).Text).IsEqualTo("output of ls");
    }

    [Test]
    public async Task RunAgentAsync_NoFinalResponse_ThrowsWithProviderError()
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
            await foreach (var _ in Loop.RunAgentAsync(
                fake, "test", "", messages, [], NeverCalledExecutor)) { }
        }).Throws<InvalidOperationException>();

        await Assert.That(ex!.Message).Contains("HTTP 401");
    }

    [Test]
    public async Task RunAgentAsync_ToolThrows_LoopCatchesAndSurfacesAsIsError()
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

        Task<ToolResult> Boom(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => throw new InvalidOperationException("kaboom");

        var events = new List<HarnessEvent>();
        await foreach (var ev in Loop.RunAgentAsync(
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
    public async Task RunAgentAsync_UnknownTool_ReturnsErrorResultWithoutThrowing()
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

        Task<ToolResult> Missing(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("Unknown tool: mystery")], IsError: true));

        await foreach (var _ in Loop.RunAgentAsync(
            fake, "test", "", messages, [], Missing)) { }

        var result = (ToolResultMessage)messages[2];
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).IsEqualTo("Unknown tool: mystery");
    }

    [Test]
    public async Task RunAgentAsync_PreservesToolResultDetails()
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

        await foreach (var _ in Loop.RunAgentAsync(
            fake, "test", "", messages, [], WithDetails)) { }

        var result = (ToolResultMessage)messages[2];
        await Assert.That(result.Details).IsNotNull();
        await Assert.That(result.Details!["path"]!.GetValue<string>()).IsEqualTo("/tmp/x");
        await Assert.That(result.Details!["lines"]!.GetValue<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task RunAgentAsync_MaxTurnsExceeded_StopsWithErrorMessage()
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

        Task<ToolResult> LoopForever(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("ok")]));

        var events = new List<HarnessEvent>();
        await foreach (var ev in Loop.RunAgentAsync(
            fake, "test", "", messages, [], LoopForever, maxTurns: 2))
        {
            events.Add(ev);
        }

        await Assert.That(events.OfType<TurnEndEvent>().Count()).IsEqualTo(1);
        var final = ((TurnEndEvent)events.Last()).FinalMessage;
        await Assert.That(final.StopReason).IsEqualTo(StopReasons.Error);
        await Assert.That(final.Text).Contains("max_turns=2");

        // user + 2 × (assistant + tool_result) + assistant(error) = 6
        await Assert.That(messages.Count).IsEqualTo(6);
        await Assert.That(messages.Last()).IsTypeOf<AssistantMessage>();
    }

    [Test]
    public async Task RunAgentAsync_Cancellation_Propagates()
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

        Task<ToolResult> Cancellable(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => Task.FromCanceled<ToolResult>(cancellationToken);

        await Assert.That(async () =>
        {
            await foreach (var _ in Loop.RunAgentAsync(
                fake, "test", "", messages, [], Cancellable, cancellationToken: cts.Token)) { }
        }).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RunAgentAsync_ThinkingLifecycle_IsTranslatedToStartDeltaEndEvents()
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
        await foreach (var ev in Loop.RunAgentAsync(
            fake, "test", "", messages, [], NeverCalledExecutor))
        {
            events.Add(ev);
        }

        var starts = events.OfType<AssistantThinkingStartEvent>().ToList();
        await Assert.That(starts.Count).IsEqualTo(1);

        var deltas = events.OfType<AssistantThinkingDeltaEvent>().ToList();
        await Assert.That(deltas.Count).IsEqualTo(2);
        await Assert.That(deltas[0].Delta).IsEqualTo("reasoning about...");
        await Assert.That(deltas[1].Delta).IsEqualTo("so my answer is 42.");

        var ends = events.OfType<AssistantThinkingEndEvent>().ToList();
        await Assert.That(ends.Count).IsEqualTo(1);
        await Assert.That(ends[0].Block.ThinkingSignature).IsEqualTo("sig-xyz");

        var turnEnd = events.OfType<TurnEndEvent>().Single();
        await Assert.That(turnEnd.FinalMessage.Content.OfType<ThinkingBlock>().Single())
            .IsEquivalentTo(consolidated);
    }

    // ──────────────────── Steering / follow-up queue tests ────────────────────

    [Test]
    public async Task RunAgentAsync_NoQueues_TerminatesAfterFirstTurn()
    {
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("done"),
                new ProviderResponseEndEvent(FinalMessage("done"), StopReasons.Stop),
            },
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "Hi" } };

        var turnStarts = new List<TurnStartEvent>();
        await foreach (var ev in Loop.RunAgentAsync(
            fake, "test", "", messages, [], NeverCalledExecutor))
        {
            if (ev is TurnStartEvent ts) turnStarts.Add(ts);
        }

        await Assert.That(turnStarts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RunAgentAsync_SteeringMessage_IsInjectedBeforeTurnRuns()
    {
        // Steering is checked at the top of every iteration, BEFORE the
        // turn counter advances. So a steering message enqueued before the
        // first turn becomes part of `messages` before the model sees them.
        // The steering iteration itself does not consume a turn slot
        // (no TurnStartEvent for it) — matching tau's run_agent_loop.
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("First"),
                new ProviderResponseEndEvent(FinalMessage("First"), StopReasons.Stop),
            },
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "first" } };

        var steeringFired = false;
        Func<IReadOnlyList<IAgentMessage>> getSteering = () =>
        {
            if (steeringFired) return [];
            steeringFired = true;
            return [new UserMessage { Content = "Actually do this" }];
        };

        var turnStarts = new List<TurnStartEvent>();
        await foreach (var ev in Loop.RunAgentAsync(
            fake, "test", "", messages, [], NeverCalledExecutor,
            getSteeringMessages: getSteering))
        {
            if (ev is TurnStartEvent ts) turnStarts.Add(ts);
        }

        // Only one real turn (steering iteration doesn't emit TurnStartEvent).
        await Assert.That(turnStarts.Count).IsEqualTo(1);
        await Assert.That(turnStarts[0].Turn).IsEqualTo(1);

        // Messages: user(initial), user(steering), assistant(turn1)
        // — steering landed in messages BEFORE the model was called.
        await Assert.That(messages.Count).IsEqualTo(3);
        await Assert.That(messages.OfType<UserMessage>().Count()).IsEqualTo(2);
        await Assert.That(messages[0]).IsTypeOf<UserMessage>();
        await Assert.That(((UserMessage)messages[0]).Text).IsEqualTo("first");
        await Assert.That(messages[1]).IsTypeOf<UserMessage>();
        await Assert.That(((UserMessage)messages[1]).Text).IsEqualTo("Actually do this");
    }

    [Test]
    public async Task RunAgentAsync_FollowUpMessage_AlsoTriggersAnotherTurn()
    {
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Done turn 1"),
                new ProviderResponseEndEvent(FinalMessage("Done turn 1"), StopReasons.Stop),
            },
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Done turn 2"),
                new ProviderResponseEndEvent(FinalMessage("Done turn 2"), StopReasons.Stop),
            },
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "first" } };

        var followUpFired = false;
        Func<IReadOnlyList<IAgentMessage>> getFollowUp = () =>
        {
            if (followUpFired) return [];
            followUpFired = true;
            return [new UserMessage { Content = "follow up" }];
        };

        var turnStarts = new List<TurnStartEvent>();
        await foreach (var ev in Loop.RunAgentAsync(
            fake, "test", "", messages, [], NeverCalledExecutor,
            getFollowUpMessages: getFollowUp))
        {
            if (ev is TurnStartEvent ts) turnStarts.Add(ts);
        }

        await Assert.That(turnStarts.Count).IsEqualTo(2);
        await Assert.That(messages.OfType<UserMessage>().Count()).IsEqualTo(2);
        await Assert.That(((UserMessage)messages[2]).Text).IsEqualTo("follow up");
    }

    [Test]
    public async Task RunAgentAsync_EmptySteering_DoesNotInterfereWithNaturalTurnFlow()
    {
        // Steering callback that always returns []. The model produces a
        // tool call in turn 1, then a final text in turn 2. Empty steering
        // must not prevent the second turn from running — turn continuation
        // is driven by tool calls, not by steering queue contents.
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{}""")!.AsObject(),
        };

        var fake = new FakePhiProvider(
        [
            new ProviderEvent[]
            {
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            },
            new ProviderEvent[]
            {
                new ProviderResponseEndEvent(FinalMessage("done"), StopReasons.Stop),
            },
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };
        Func<IReadOnlyList<IAgentMessage>> getSteering = () => [];

        Task<ToolResult> Noop(string toolName, string toolCallId, JsonNode arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("ok")]));

        var turnStarts = new List<TurnStartEvent>();
        await foreach (var ev in Loop.RunAgentAsync(
            fake, "test", "", messages, [], Noop,
            getSteeringMessages: getSteering))
        {
            if (ev is TurnStartEvent ts) turnStarts.Add(ts);
        }

        await Assert.That(turnStarts.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RunAgentAsync_SteeringPriority_BeatsFollowUp()
    {
        // Both queues have a message waiting. Steering must be drained first,
        // matching tau's run_agent_loop order: check steering → run turn →
        // check follow-up. Each callback fires exactly once so the loop
        // terminates after both queues are drained.
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[]
            {
                new ProviderResponseEndEvent(FinalMessage("t1"), StopReasons.Stop),
            },
            new ProviderEvent[]
            {
                new ProviderResponseEndEvent(FinalMessage("t2"), StopReasons.Stop),
            },
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "x" } };
        var steeringFired = false;
        var followUpFired = false;
        Func<IReadOnlyList<IAgentMessage>> getSteering = () =>
        {
            if (steeringFired) return [];
            steeringFired = true;
            return [new UserMessage { Content = "steering-first" }];
        };
        Func<IReadOnlyList<IAgentMessage>> getFollowUp = () =>
        {
            if (followUpFired) return [];
            followUpFired = true;
            return [new UserMessage { Content = "follow-up-second" }];
        };

        await foreach (var _ in Loop.RunAgentAsync(
            fake, "test", "", messages, [], NeverCalledExecutor,
            getSteeringMessages: getSteering,
            getFollowUpMessages: getFollowUp)) { }

        var userMessages = messages.OfType<UserMessage>().ToList();
        await Assert.That(userMessages.Count).IsEqualTo(3);
        await Assert.That(userMessages[1].Text).IsEqualTo("steering-first");
        await Assert.That(userMessages[2].Text).IsEqualTo("follow-up-second");
    }
}
