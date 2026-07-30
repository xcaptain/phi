using PhiAgent;
using System.Text.Json.Nodes;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiAgent.Tests;

public class HarnessInterruptedToolTests
{
    private static ToolCall MakeToolCall(string id, string name = "bash") =>
        new(id, name)
        {
            Arguments = JsonNode.Parse("""{"command":"ls"}""")!.AsObject(),
        };

    [Test]
    public async Task AppendInterruptedToolResults_NoUnreturnedToolCalls_ReturnsZero()
    {
        var harness = new Harness(new FakePhiProvider([]), Array.Empty<IHarnessTool>(), "test");
        harness.AppendMessage(new AssistantMessage
        {
            Content = [new TextBlock("no tools here")],
        });

        var inserted = harness.AppendInterruptedToolResults();

        await Assert.That(inserted).IsEqualTo(0);
        await Assert.That(harness.Messages.OfType<ToolResultMessage>().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task AppendInterruptedToolResults_ToolWithNoResult_InsertsInterruptedPlaceholder()
    {
        var harness = new Harness(new FakePhiProvider([]), Array.Empty<IHarnessTool>(), "test");
        var call = MakeToolCall("c1");
        harness.AppendMessage(new AssistantMessage
        {
            Content = [call],
            StopReason = StopReasons.ToolUse,
        });

        var inserted = harness.AppendInterruptedToolResults();

        await Assert.That(inserted).IsEqualTo(1);
        var result = harness.Messages.OfType<ToolResultMessage>().Single();
        await Assert.That(result.ToolCallId).IsEqualTo("c1");
        await Assert.That(result.ToolName).IsEqualTo("bash");
        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("interrupted");
    }

    [Test]
    public async Task AppendInterruptedToolResults_ToolWithExistingResult_IsNotDuplicated()
    {
        var harness = new Harness(new FakePhiProvider([]), Array.Empty<IHarnessTool>(), "test");
        var call = MakeToolCall("c1");
        harness.AppendMessage(new AssistantMessage { Content = [call] });
        harness.AppendMessage(new ToolResultMessage
        {
            ToolCallId = "c1",
            ToolName = "bash",
            Content = [new TextBlock("real output")],
        });

        var inserted = harness.AppendInterruptedToolResults();

        await Assert.That(inserted).IsEqualTo(0);
        await Assert.That(harness.Messages.OfType<ToolResultMessage>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task AppendInterruptedToolResults_MultipleAssistantTurns_HandlesEachIndependently()
    {
        var harness = new Harness(new FakePhiProvider([]), Array.Empty<IHarnessTool>(), "test");

        // Turn 1: assistant has 2 tool calls, one returned, one not
        harness.AppendMessage(new AssistantMessage
        {
            Content = [MakeToolCall("a"), MakeToolCall("b")],
        });
        harness.AppendMessage(new ToolResultMessage { ToolCallId = "a", ToolName = "bash" });

        // Turn 2: assistant has 1 tool call, not returned
        harness.AppendMessage(new AssistantMessage
        {
            Content = [MakeToolCall("c")],
        });

        var inserted = harness.AppendInterruptedToolResults();

        // Only "b" and "c" need placeholders
        await Assert.That(inserted).IsEqualTo(2);
        var results = harness.Messages.OfType<ToolResultMessage>().ToList();
        await Assert.That(results.Count).IsEqualTo(3);
        await Assert.That(results.Select(r => r.ToolCallId).OrderBy(id => id))
            .IsEquivalentTo(["a", "b", "c"]);
    }

    [Test]
    public async Task AppendInterruptedToolResults_CalledTwice_IsIdempotent()
    {
        var harness = new Harness(new FakePhiProvider([]), Array.Empty<IHarnessTool>(), "test");
        harness.AppendMessage(new AssistantMessage { Content = [MakeToolCall("c1")] });

        var first = harness.AppendInterruptedToolResults();
        var second = harness.AppendInterruptedToolResults();

        await Assert.That(first).IsEqualTo(1);
        await Assert.That(second).IsEqualTo(0);
        await Assert.That(harness.Messages.OfType<ToolResultMessage>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_CancellationSurfacesAsHarnessErrorEvent_DoesNotThrow()
    {
        // Pre-cancelled token + one queued event so FakePhiProvider yields
        // at least once before its ThrowIfCancellationRequested fires. The
        // harness should swallow the OCE, surface it as a HarnessErrorEvent,
        // and end the session normally — never re-throwing to the caller.
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[] { new ProviderTextDeltaEvent("hel") },
        ]);
        var harness = new Harness(fake, Array.Empty<IHarnessTool>(), "test");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var events = new List<HarnessEvent>();
        await foreach (var ev in harness.RunAsync("hi", cancellationToken: cts.Token))
        {
            events.Add(ev);
        }

        var errors = events.OfType<HarnessErrorEvent>().ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).IsEqualTo("interrupted");

        // User prompt still landed in messages even though the turn aborted
        // at the provider boundary — the caller can resume by inspecting
        // Harness.Messages.
        await Assert.That(harness.Messages.OfType<UserMessage>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task ReplaceMessages_ReplacesAllMessages()
    {
        var harness = new Harness(new FakePhiProvider([]), Array.Empty<IHarnessTool>(), "test");
        harness.AppendMessage(new UserMessage { Content = "first" });
        harness.AppendMessage(new AssistantMessage { Content = [new TextBlock("resp")] });

        await Assert.That(harness.Messages.Count).IsEqualTo(2);

        var replacement = new IAgentMessage[]
        {
            new UserMessage { Content = "new" },
        };
        harness.ReplaceMessages(replacement);

        await Assert.That(harness.Messages.Count).IsEqualTo(1);
        await Assert.That(((UserMessage)harness.Messages[0]).Text).IsEqualTo("new");
    }

    [Test]
    public async Task ReplaceMessages_EmptyClearsAll()
    {
        var harness = new Harness(new FakePhiProvider([]), Array.Empty<IHarnessTool>(), "test");
        harness.AppendMessage(new UserMessage { Content = "x" });

        harness.ReplaceMessages([]);

        await Assert.That(harness.Messages).IsEmpty();
    }

    [Test]
    public async Task RunAsync_CancellationWithInterruptedTool_InsertsPlaceholderViaIntegration()
    {
        // End-to-end: pre-seed the harness with a partial assistant message
        // (as if the model streamed a tool call then the user hit Esc mid-tool),
        // then run with a cancelled token. RunAsync's catch path should call
        // AppendInterruptedToolResults, leaving the message chain well-formed.
        var toolCall = MakeToolCall("c1");
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[] { new ProviderTextDeltaEvent("x") },
        ]);
        var harness = new Harness(fake, Array.Empty<IHarnessTool>(), "test");
        harness.AppendMessage(new AssistantMessage
        {
            Content = [new TextBlock("thinking…"), toolCall],
            StopReason = StopReasons.ToolUse,
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var events = new List<HarnessEvent>();
        await foreach (var ev in harness.RunAsync("hi", cancellationToken: cts.Token))
        {
            events.Add(ev);
        }

        var errors = events.OfType<HarnessErrorEvent>().ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("1 tool call");

        var result = harness.Messages.OfType<ToolResultMessage>().Single();
        await Assert.That(result.ToolCallId).IsEqualTo("c1");
        await Assert.That(result.IsError).IsTrue();
    }

    [Test]
    public async Task RunAsync_CancellationWithNoInterruptedTools_YieldsGenericInterruptedMessage()
    {
        // Cancel during text streaming — no tool was in flight, so the
        // placeholders list is empty. We should still surface the cancel
        // as a HarnessErrorEvent for the UI to render.
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[] { new ProviderTextDeltaEvent("hel") },
        });

        var harness = new Harness(fake, Array.Empty<IHarnessTool>(), "test");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var events = new List<HarnessEvent>();
        await foreach (var ev in harness.RunAsync("hi", cancellationToken: cts.Token))
        {
            events.Add(ev);
        }

        await Assert.That(events.OfType<HarnessErrorEvent>().Count()).IsEqualTo(1);
        await Assert.That(events.OfType<HarnessErrorEvent>().Single().Message)
            .IsEqualTo("interrupted");
    }
}
