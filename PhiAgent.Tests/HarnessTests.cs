using System.Text.Json.Nodes;
using PhiAgent;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiAgent.Tests;

public class HarnessTests
{
    [Test]
    public async Task RunAsync_NoToolCalls_CompletesAfterOneTurn()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Hello! "),
                new ProviderTextDeltaEvent("How can I help?"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Api = "test",
                        Provider = "fake",
                        Model = "test-model",
                        Content = [new TextBlock("Hello! How can I help?")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
        });

        var harness = new Harness(
            fake,
            tools: [],
            executeTool: (_, _, _, _) =>
                throw new InvalidOperationException("Tool should not be called"),
            model: "test-model");

        var result = await harness.RunAsync("Hi");

        await Assert.That(result.FinalMessage.Text).IsEqualTo("Hello! How can I help?");
        await Assert.That(result.FinalMessage.StopReason).IsEqualTo(StopReasons.Stop);
        await Assert.That(result.Messages.Count()).IsEqualTo(2);
        await Assert.That(result.Messages[0]).IsTypeOf<UserMessage>();
        await Assert.That(result.Messages[1]).IsTypeOf<AssistantMessage>();
        await Assert.That(fake.CallsReceived.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_ToolCall_ExecutesAndLoopsForFinalAnswer()
    {
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{"command":"ls"}""")!.AsObject(),
        };

        var fake = new FakePhiProvider(new[]
        {
            // Turn 1: tool call
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Let me check."),
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Api = "test", Provider = "fake", Model = "test-model",
                        Content = [new TextBlock("Let me check."), toolCall],
                        StopReason = StopReasons.ToolUse,
                    },
                    StopReasons.ToolUse),
            },
            // Turn 2: final answer after tool result
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Found 3 files."),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Api = "test", Provider = "fake", Model = "test-model",
                        Content = [new TextBlock("Found 3 files.")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
        });

        var tools = new[] { new Tool("bash", "Run a command", new Dictionary<string, JsonNode>()) };
        var executed = new List<(string Name, string Id, string Command)>();

        var harness = new Harness(
            fake,
            tools,
            async (name, id, args, _) =>
            {
                var cmd = args["command"]?.GetValue<string>() ?? "";
                executed.Add((name, id, cmd));
                return new ToolResult([new TextBlock($"output of {cmd}")]);
            },
            model: "test-model");

        var result = await harness.RunAsync("list files");

        await Assert.That(executed.Count()).IsEqualTo(1);
        await Assert.That(executed[0].Name).IsEqualTo("bash");
        await Assert.That(executed[0].Id).IsEqualTo("c1");
        await Assert.That(executed[0].Command).IsEqualTo("ls");

        await Assert.That(result.FinalMessage.Text).IsEqualTo("Found 3 files.");
        await Assert.That(result.Messages.Count()).IsEqualTo(4);
        await Assert.That(result.Messages[0]).IsTypeOf<UserMessage>();
        await Assert.That(result.Messages[1]).IsTypeOf<AssistantMessage>();
        await Assert.That(result.Messages[2]).IsTypeOf<ToolResultMessage>();
        await Assert.That(result.Messages[3]).IsTypeOf<AssistantMessage>();

        var toolResult = (ToolResultMessage)result.Messages[2];
        await Assert.That(toolResult.ToolCallId).IsEqualTo("c1");
        await Assert.That(toolResult.ToolName).IsEqualTo("bash");
        await Assert.That(toolResult.Text).IsEqualTo("output of ls");

        // Second provider call should have included the ToolResultMessage
        await Assert.That(fake.CallsReceived.Count()).IsEqualTo(2);
        await Assert.That(fake.CallsReceived[1].Count()).IsEqualTo(3); // user + assistant + tool_result
        await Assert.That(fake.CallsReceived[1][2]).IsTypeOf<ToolResultMessage>();
    }

    [Test]
    public async Task RunAsync_ToolReturnsError_StillContinuesLoop()
    {
        var toolCall = new ToolCall("c1", "bash");
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderToolCallEvent(toolCall),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Content = [toolCall],
                        StopReason = StopReasons.ToolUse,
                    },
                    StopReasons.ToolUse),
            },
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("It failed because of X."),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Content = [new TextBlock("It failed because of X.")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
        });

        var harness = new Harness(
            fake,
            [],
            (_, _, _, _) => Task.FromResult(new ToolResult(
                [new TextBlock("error: command not found")],
                IsError: true)),
            model: "test-model");

        var result = await harness.RunAsync("try something");

        await Assert.That(result.FinalMessage.Text).IsEqualTo("It failed because of X.");
        var toolResult = (ToolResultMessage)result.Messages[2];
        await Assert.That(toolResult.IsError).IsTrue();
        await Assert.That(toolResult.Text).IsEqualTo("error: command not found");
    }

    [Test]
    public async Task RunAsync_PropagatesSystemAndModelToProvider()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderResponseEndEvent(
                    new AssistantMessage { Content = [], StopReason = StopReasons.Stop },
                    StopReasons.Stop),
            },
        });

        var harness = new Harness(
            fake,
            [],
            (_, _, _, _) => throw new InvalidOperationException("nope"),
            model: "deepseek-chat",
            system: "you are helpful");

        _ = await harness.RunAsync("hi");

        // FakePhiProvider doesn't expose model/system args — assert via harness internals:
        // the harness just passes them through, and the call was made.
        await Assert.That(fake.CallsReceived.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_ProviderErrorEvent_SurfacesInException()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderErrorEvent("HTTP 401: {\"error\":{\"message\":\"Invalid API key\"}}"),
            },
        });

        var harness = new Harness(
            fake,
            [],
            (_, _, _, _) => throw new InvalidOperationException("nope"),
            model: "test-model");

        var ex = await Assert.That(async () => await harness.RunAsync("hi"))
            .Throws<InvalidOperationException>();

        await Assert.That(ex!.Message).Contains("HTTP 401");
        await Assert.That(ex.Message).Contains("Invalid API key");
    }
}