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
    public async Task RunTurnAsync_NoToolCalls_EmitsTextDeltasAndTurnEnd()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Hello"),
                new ProviderTextDeltaEvent(" world"),
                new ProviderResponseEndEvent(FinalMessage("Hello world"), StopReasons.Stop),
            },
        });

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

        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Let me check"),
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall, "Let me check"), StopReasons.ToolUse),
            },
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("3 files"),
                new ProviderResponseEndEvent(FinalMessage("3 files"), StopReasons.Stop),
            },
        });

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

        await Assert.That(executed).IsEquivalentTo(new[] { "ls" });

        await Assert.That(events.OfType<AssistantToolCallEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<ToolExecutionStartEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<ToolExecutionEndEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<TurnEndEvent>().Count()).IsEqualTo(1);

        // Event order check
        var kinds = events.Select(e => e.GetType().Name).ToList();
        await Assert.That(kinds).IsEquivalentTo(new[]
        {
            "AssistantTextDeltaEvent",
            "AssistantToolCallEvent",
            "ToolExecutionStartEvent",
            "ToolExecutionEndEvent",
            "AssistantTextDeltaEvent",
            "TurnEndEvent",
        });

        // Messages: user + assistant(tool call) + tool_result + assistant(final) = 4
        await Assert.That(messages.Count).IsEqualTo(4);
        await Assert.That(messages[2]).IsTypeOf<ToolResultMessage>();
        await Assert.That(((ToolResultMessage)messages[2]).Text).IsEqualTo("output of ls");
    }

    [Test]
    public async Task RunTurnAsync_NoFinalResponse_ThrowsWithProviderError()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderErrorEvent("HTTP 401: invalid key"),
            },
        });

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

        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            },
            new ProviderEvent[]
            {
                new ProviderResponseEndEvent(FinalMessage("done"), StopReasons.Stop),
            },
        });

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };

        ToolExecutor boom = (_, _, _, _) => throw new InvalidOperationException("kaboom");

        var events = new List<HarnessEvent>();
        await foreach (var ev in Loop.RunTurnAsync(
            fake, "test", "", messages, [], boom))
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

        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            },
            new ProviderEvent[]
            {
                new ProviderResponseEndEvent(FinalMessage("ok"), StopReasons.Stop),
            },
        });

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };

        ToolExecutor missing = (_, _, _, _) =>
            Task.FromResult(new ToolResult([new TextBlock("Unknown tool: mystery")], IsError: true));

        await foreach (var _ in Loop.RunTurnAsync(
            fake, "test", "", messages, [], missing)) { }

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

        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            },
            new ProviderEvent[]
            {
                new ProviderResponseEndEvent(FinalMessage("done"), StopReasons.Stop),
            },
        });

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };
        var details = JsonNode.Parse("""{"path":"/tmp/x","lines":42}""")!;

        ToolExecutor withDetails = (_, _, _, _) =>
            Task.FromResult(new ToolResult([new TextBlock("ok")], Details: details));

        await foreach (var _ in Loop.RunTurnAsync(
            fake, "test", "", messages, [], withDetails)) { }

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

        var fake = new FakePhiProvider(Enumerable.Range(0, 5).Select(_ =>
            (IEnumerable<ProviderEvent>)new ProviderEvent[]
            {
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            }).ToArray());

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };

        ToolExecutor loopForever = (_, _, _, _) => Task.FromResult(new ToolResult([new TextBlock("ok")]));

        var events = new List<HarnessEvent>();
        await foreach (var ev in Loop.RunTurnAsync(
            fake, "test", "", messages, [], loopForever, maxTurns: 2))
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

        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(ToolUseMessage(toolCall), StopReasons.ToolUse),
            },
        });

        var messages = new List<IAgentMessage> { new UserMessage { Content = "go" } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        ToolExecutor cancellable = (_, _, _, ct) =>
            Task.FromCanceled<ToolResult>(ct);

        await Assert.That(async () =>
        {
            await foreach (var _ in Loop.RunTurnAsync(
                fake, "test", "", messages, [], cancellable, cancellationToken: cts.Token)) { }
        }).Throws<OperationCanceledException>();
    }
}