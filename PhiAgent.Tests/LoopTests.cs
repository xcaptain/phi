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

    [Test]
    public async Task RunTurnAsync_NoToolCalls_EmitsTextDeltasAndTurnEnd()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Hello"),
                new ProviderTextDeltaEvent(" world"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Api = "test", Provider = "fake", Model = "test",
                        Content = [new TextBlock("Hello world")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
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
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Api = "test", Provider = "fake", Model = "test",
                        Content = [new TextBlock("Let me check"), toolCall],
                        StopReason = StopReasons.ToolUse,
                    },
                    StopReasons.ToolUse),
            },
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("3 files"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Api = "test", Provider = "fake", Model = "test",
                        Content = [new TextBlock("3 files")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
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
}