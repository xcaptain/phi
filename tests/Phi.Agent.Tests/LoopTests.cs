using System.Text.Json.Nodes;

namespace Phi.Agent.Tests;

public class LoopTests
{
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

    private static async Task<List<HarnessEvent>> RunAsync(
        FakePhiProvider fake, IList<IAgentMessage> messages,
        IReadOnlyList<Tool>? tools = null,
        int? maxTurns = null,
        Func<IReadOnlyList<IAgentMessage>>? getSteeringMessages = null,
        Func<IReadOnlyList<IAgentMessage>>? getFollowUpMessages = null,
        CancellationToken cancellationToken = default)
    {
        var events = new List<HarnessEvent>();
        await foreach (var ev in AgentLoop.RunAgentAsync(
            fake, "test", "", messages, tools ?? [],
            getSteeringMessages, getFollowUpMessages,
            maxTurns, cancellationToken))
        {
            events.Add(ev);
        }
        return events;
    }

    [Test]
    public async Task RunAgentAsync_NoToolCalls_EmitsAgentTurnAndMessageEnvelope()
    {
        var fake = new FakePhiProvider(
        [
            [
                new TextDeltaEvent("Hello"),
                new TextDeltaEvent(" world"),
                new AssistantDoneEvent(FinalMessage("Hello world"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "Hi" } };
        var events = await RunAsync(fake, messages);

        await Assert.That(events[0]).IsTypeOf<AgentStartEvent>();
        await Assert.That(events.OfType<MessageUpdateEvent>().Count()).IsEqualTo(2);
        await Assert.That(events.OfType<MessageUpdateEvent>().All(e => e.ProviderEvent is TextDeltaEvent)).IsTrue();
        await Assert.That(events.OfType<ToolExecutionStartEvent>()).IsEmpty();
        await Assert.That(events.OfType<ToolExecutionEndEvent>()).IsEmpty();
        await Assert.That(events.OfType<TurnEndEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<TurnEndEvent>().Single().Message.Text).IsEqualTo("Hello world");
        await Assert.That(events[^1]).IsTypeOf<AgentEndEvent>();

        await Assert.That(messages.Count).IsEqualTo(2);
        await Assert.That(messages[0]).IsTypeOf<UserMessage>();
        await Assert.That(messages[1]).IsTypeOf<AssistantMessage>();
    }

    [Test]
    public async Task RunAgentAsync_FinalMessageAdoptsProviderIdentity()
    {
        // The loop must not fabricate provider identity on the partial
        // (no Provider="agent" / Api=<class name>); the real values ride on
        // the provider's terminal message and are adopted at terminal.
        var fake = new FakePhiProvider(
        [
            [
                new TextDeltaEvent("Hello"),
                new AssistantDoneEvent(FinalMessage("Hello"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "Hi" } };
        var events = await RunAsync(fake, messages);

        var final = events.OfType<TurnEndEvent>().Single().Message;
        await Assert.That(final.Api).IsEqualTo("test");
        await Assert.That(final.Provider).IsEqualTo("fake");
        // Streaming updates carry the pre-terminal partial: identity stays
        // at the "unknown" default until AdoptFinal lands.
        var streamed = events.OfType<MessageUpdateEvent>().First().Message;
        await Assert.That(streamed.Api).IsEqualTo("unknown");
        await Assert.That(streamed.Provider).IsEqualTo("unknown");
    }

    [Test]
    public async Task RunAgentAsync_MaxTurnsOverrun_MessageHasUnknownIdentity()
    {
        // Mirrors tau's _error_message: agent-synthesized messages set
        // model + stop_reason only; Api/Provider stay "unknown".
        var fake = new FakePhiProvider(
        [
            [
                new TextDeltaEvent("t1"),
                new AssistantDoneEvent(FinalMessage("t1"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "Hi" } };
        var events = await RunAsync(fake, messages, maxTurns: 0);

        var overrun = events.OfType<TurnEndEvent>().Single().Message;
        await Assert.That(overrun.StopReason).IsEqualTo(StopReasons.Error);
        await Assert.That(overrun.Api).IsEqualTo("unknown");
        await Assert.That(overrun.Provider).IsEqualTo("unknown");
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
                new TextDeltaEvent("Let me check"),
                new ToolCallEvent(toolCall),
                new AssistantDoneEvent(ToolUseMessage(toolCall, "Let me check"), StopReasons.ToolUse),
            ],
            [
                new TextDeltaEvent("3 files"),
                new AssistantDoneEvent(FinalMessage("3 files"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "list files" } };

        var executed = new List<string>();
        Task<ToolResult> Bash(string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
        {
            var cmd = arguments["command"]?.GetValue<string>() ?? "";
            executed.Add(cmd);
            return Task.FromResult(new ToolResult([new TextBlock($"output of {cmd}")]));
        }

        var events = await RunAsync(fake, messages, [new FuncTool("bash", Bash)]);

        await Assert.That(executed).IsEquivalentTo(["ls"]);

        // Turn 1: MessageStart → MessageUpdate(text) → MessageUpdate(toolcall)
        //        → MessageEnd → ToolExecStart → ToolExecEnd →
        //        MessageStart(tool_result) → MessageEnd → TurnEnd
        // Turn 2: MessageStart → MessageUpdate(text) → MessageEnd → TurnEnd
        //        → AgentEnd
        await Assert.That(events.OfType<MessageUpdateEvent>().Count()).IsEqualTo(3);
        await Assert.That(events.OfType<ToolExecutionStartEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<ToolExecutionEndEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<TurnEndEvent>().Count()).IsEqualTo(2);
        // MessageStart envelopes: assistant turn 1, tool result, assistant turn 2.
        await Assert.That(events.OfType<MessageStartEvent>().Count()).IsEqualTo(3);
        await Assert.That(events.OfType<MessageEndEvent>().Count()).IsEqualTo(3);

        var kinds = events.Select(e => e.GetType().Name).ToList();
        await Assert.That(kinds).IsEquivalentTo(
        [
            "AgentStartEvent",
            "TurnStartEvent",          // turn 1
            "MessageStartEvent",       // assistant (turn 1)
            "MessageUpdateEvent",       // "Let me check"
            "MessageUpdateEvent",       // tool_call
            "MessageEndEvent",
            "ToolExecutionStartEvent",
            "ToolExecutionEndEvent",
            "MessageStartEvent",       // tool_result
            "MessageEndEvent",
            "TurnEndEvent",
            "TurnStartEvent",          // turn 2
            "MessageStartEvent",       // assistant (turn 2)
            "MessageUpdateEvent",       // "3 files"
            "MessageEndEvent",
            "TurnEndEvent",
            "AgentEndEvent",
        ]);

        await Assert.That(messages.Count).IsEqualTo(4);
        await Assert.That(messages[2]).IsTypeOf<ToolResultMessage>();
        await Assert.That(((ToolResultMessage)messages[2]).Text).IsEqualTo("output of ls");
    }

    [Test]
    public async Task RunAgentAsync_NoFinalResponse_SynthesizesErrorAssistantMessage()
    {
        // The provider stream ended after an error event, without a final
        // response. Matching tau's canonicalize_provider_stream, the loop
        // turns this into a terminal assistant message with StopReason=Error
        // (preserving the last provider error) instead of throwing.
        var fake = new FakePhiProvider(
        [
            [
                new AssistantErrorEvent("HTTP 401: invalid key"),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "hi" } };
        var events = await RunAsync(fake, messages);

        var turnEnd = events.OfType<TurnEndEvent>().Single();
        await Assert.That(turnEnd.Message.StopReason).IsEqualTo(StopReasons.Error);
        await Assert.That(turnEnd.Message.ErrorMessage).Contains("HTTP 401");

        // The failure is appended to history (for diagnostics), and the loop
        // stops — no tool execution, no further turns.
        var assistant = messages.OfType<AssistantMessage>().Single();
        await Assert.That(assistant.StopReason).IsEqualTo(StopReasons.Error);
        await Assert.That(events.OfType<TurnStartEvent>().Count()).IsEqualTo(1);
        await Assert.That(events[^1]).IsTypeOf<AgentEndEvent>();
    }

    [Test]
    public async Task RunAgentAsync_EmptyStream_SynthesizesErrorAssistantMessage()
    {
        // Defensive: the provider completed without any events at all — no
        // deltas, no error, no response end. Still a terminal error message.
        var fake = new FakePhiProvider(
        [
            [],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "hi" } };
        var events = await RunAsync(fake, messages);

        var turnEnd = events.OfType<TurnEndEvent>().Single();
        await Assert.That(turnEnd.Message.StopReason).IsEqualTo(StopReasons.Error);
        await Assert.That(turnEnd.Message.ErrorMessage)
            .Contains("Stream ended without a final response");
    }

    [Test]
    public async Task RunAgentAsync_ToolThrows_LoopCatchesAndSurfacesAsIsError()
    {
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{}""")!.AsObject(),
        };

        var fake = new FakePhiProvider(
        [
            [
                new ToolCallEvent(toolCall),
                new AssistantDoneEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ],
            [
                new TextDeltaEvent("done"),
                new AssistantDoneEvent(FinalMessage("done"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };

        Task<ToolResult> Boom(string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
            => throw new InvalidOperationException("kaboom");

        var events = await RunAsync(fake, messages, [new FuncTool("bash", Boom)]);

        var result = (ToolResultMessage)messages[2];
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("kaboom");

        await Assert.That(messages.Count).IsEqualTo(4);
        await Assert.That(messages.Last()).IsTypeOf<AssistantMessage>();
        await Assert.That(events.OfType<TurnEndEvent>().Last().Message.Text).IsEqualTo("done");
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
                new ToolCallEvent(toolCall),
                new AssistantDoneEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ],
            [
                new AssistantDoneEvent(FinalMessage("ok"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };

        Task<ToolResult> Missing(string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("Unknown tool: mystery")], IsError: true));

        await foreach (var _ in AgentLoop.RunAgentAsync(
            fake, "test", "", messages, [new FuncTool("mystery", Missing)])) { }

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
                new ToolCallEvent(toolCall),
                new AssistantDoneEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ],
            [
                new AssistantDoneEvent(FinalMessage("done"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };
        var details = JsonNode.Parse("""{"path":"/tmp/x","lines":42}""")!;

        Task<ToolResult> WithDetails(string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("ok")], Details: details));

        await foreach (var _ in AgentLoop.RunAgentAsync(
            fake, "test", "", messages, [new FuncTool("bash", WithDetails)])) { }

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
                new ToolCallEvent(toolCall),
                new AssistantDoneEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ])]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };

        Task<ToolResult> LoopForever(string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("ok")]));

        var events = await RunAsync(fake, messages, [new FuncTool("bash", LoopForever)], maxTurns: 2);

        // Turn 1 + Turn 2 each emit TurnEndEvent (tool use → next iteration);
        // turn 3 would exceed maxTurns=2 → error assistant message + TurnEnd.
        // Three TurnEndEvents total, the last one is the error.
        await Assert.That(events.OfType<TurnEndEvent>().Count()).IsEqualTo(3);
        var final = events.OfType<TurnEndEvent>().Last().Message;
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
                new ToolCallEvent(toolCall),
                new AssistantDoneEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Task<ToolResult> Cancellable(string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
            => Task.FromCanceled<ToolResult>(cancellationToken);

        await Assert.That(async () =>
        {
            await foreach (var _ in AgentLoop.RunAgentAsync(
                fake, "test", "", messages, [new FuncTool("bash", Cancellable)], cancellationToken: cts.Token)) { }
        }).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RunAgentAsync_ThinkingLifecycle_IsForwardedAsMessageUpdates()
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
                new ThinkingDeltaEvent("reasoning about..."),
                new ThinkingDeltaEvent("so my answer is 42."),
                new ThinkingEndEvent(consolidated),
                new TextDeltaEvent("The answer is 42."),
                new AssistantDoneEvent(final, StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "Hi" } };
        var events = await RunAsync(fake, messages);

        // The loop translates each provider event into a MessageUpdateEvent
        // carrying the original ProviderEvent — tau-aligned forwarding.
        // Thinking blocks open lazily on the first delta (no separate
        // ThinkingStartEvent in the protocol).
        var updates = events.OfType<MessageUpdateEvent>().ToList();
        await Assert.That(updates.Count).IsEqualTo(4);
        await Assert.That(updates[0].ProviderEvent).IsTypeOf<ThinkingDeltaEvent>();
        await Assert.That(((ThinkingDeltaEvent)updates[0].ProviderEvent).Delta)
            .IsEqualTo("reasoning about...");
        await Assert.That(((ThinkingDeltaEvent)updates[1].ProviderEvent).Delta)
            .IsEqualTo("so my answer is 42.");
        await Assert.That(updates[2].ProviderEvent).IsTypeOf<ThinkingEndEvent>();
        await Assert.That(updates[3].ProviderEvent).IsTypeOf<TextDeltaEvent>();

        // Signature from ThinkingEndEvent lands on the final partial
        // via the CloseThinkingBlock accumulator in the loop.
        var turnEnd = events.OfType<TurnEndEvent>().Single();
        var resultBlock = turnEnd.Message.Content.OfType<ThinkingBlock>().Single();
        await Assert.That(resultBlock.Thinking).IsEqualTo(consolidated.Thinking);
        await Assert.That(resultBlock.ThinkingSignature).IsEqualTo(consolidated.ThinkingSignature);
    }

    [Test]
    public async Task RunAgentAsync_AssistantStartEvent_IsNoOpAtLoopLayer()
    {
        // Pi-compatible begin marker: the loop already emitted
        // MessageStartEvent before driving the stream, so the provider's
        // AssistantStartEvent must not surface as a MessageUpdateEvent.
        var fake = new FakePhiProvider(
        [
            [
                new AssistantStartEvent(),
                new TextDeltaEvent("Hello"),
                new AssistantDoneEvent(FinalMessage("Hello"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "Hi" } };
        var events = await RunAsync(fake, messages);

        var updates = events.OfType<MessageUpdateEvent>().ToList();
        await Assert.That(updates.Count).IsEqualTo(1);
        await Assert.That(updates[0].ProviderEvent).IsTypeOf<TextDeltaEvent>();
        await Assert.That(events.OfType<TurnEndEvent>().Single().Message.Text).IsEqualTo("Hello");
    }

    [Test]
    public async Task RunAgentAsync_SteeringMessage_IsInjectedWithMessageEnvelope()
    {
        // Steering is checked at the top of every iteration, BEFORE the
        // turn counter advances. The steering iteration itself does not
        // consume a turn slot (no TurnStartEvent for it) — matching tau's
        // run_agent_loop. Each steering message gets its own
        // MessageStartEvent + MessageEndEvent envelope.
        var fake = new FakePhiProvider(
        [
            [
                new TextDeltaEvent("First"),
                new AssistantDoneEvent(FinalMessage("First"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "first" } };

        var steeringFired = false;
        Func<IReadOnlyList<IAgentMessage>> getSteering = () =>
        {
            if (steeringFired) return [];
            steeringFired = true;
            return [new UserMessage { Content = "Actually do this" }];
        };

        var events = await RunAsync(fake, messages, getSteeringMessages: getSteering);

        await Assert.That(events.OfType<TurnStartEvent>().Count()).IsEqualTo(1);

        // Steering message gets a Start/End envelope before the real turn.
        var messageStarts = events.OfType<MessageStartEvent>().ToList();
        await Assert.That(messageStarts.Count).IsEqualTo(2);  // steering + assistant
        await Assert.That(messageStarts[0].Message).IsTypeOf<UserMessage>();
        await Assert.That(((UserMessage)messageStarts[0].Message).Text).IsEqualTo("Actually do this");

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
            [
                new TextDeltaEvent("Done turn 1"),
                new AssistantDoneEvent(FinalMessage("Done turn 1"), StopReasons.Stop),
            ],
            [
                new TextDeltaEvent("Done turn 2"),
                new AssistantDoneEvent(FinalMessage("Done turn 2"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "first" } };

        var followUpFired = false;
        Func<IReadOnlyList<IAgentMessage>> getFollowUp = () =>
        {
            if (followUpFired) return [];
            followUpFired = true;
            return [new UserMessage { Content = "follow up" }];
        };

        var events = await RunAsync(fake, messages, getFollowUpMessages: getFollowUp);

        await Assert.That(events.OfType<TurnStartEvent>().Count()).IsEqualTo(2);
        await Assert.That(messages.OfType<UserMessage>().Count()).IsEqualTo(2);
        await Assert.That(((UserMessage)messages[2]).Text).IsEqualTo("follow up");
    }

    [Test]
    public async Task RunAgentAsync_EmptySteering_DoesNotInterfereWithNaturalTurnFlow()
    {
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{}""")!.AsObject(),
        };

        var fake = new FakePhiProvider(
        [
            [
                new ToolCallEvent(toolCall),
                new AssistantDoneEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            ],
            [
                new AssistantDoneEvent(FinalMessage("done"), StopReasons.Stop),
            ],
        ]);

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };
        Func<IReadOnlyList<IAgentMessage>> getSteering = () => [];

        Task<ToolResult> Noop(string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("ok")]));

        var events = await RunAsync(fake, messages, [new FuncTool("bash", Noop)],
            getSteeringMessages: getSteering);

        await Assert.That(events.OfType<TurnStartEvent>().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task RunAgentAsync_SteeringPriority_BeatsFollowUp()
    {
        var fake = new FakePhiProvider(
        [
            [
                new AssistantDoneEvent(FinalMessage("t1"), StopReasons.Stop),
            ],
            [
                new AssistantDoneEvent(FinalMessage("t2"), StopReasons.Stop),
            ],
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

        await foreach (var _ in AgentLoop.RunAgentAsync(
            fake, "test", "", messages, [],
            getSteeringMessages: getSteering,
            getFollowUpMessages: getFollowUp)) { }

        var userMessages = messages.OfType<UserMessage>().ToList();
        await Assert.That(userMessages.Count).IsEqualTo(3);
        await Assert.That(userMessages[1].Text).IsEqualTo("steering-first");
        await Assert.That(userMessages[2].Text).IsEqualTo("follow-up-second");
    }
}
