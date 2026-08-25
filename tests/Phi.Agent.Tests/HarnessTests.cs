using System.Text.Json.Nodes;

namespace Phi.Agent.Tests;

public class HarnessTests
{
    private static Harness CreateHarness(FakePhiProvider fake) =>
        new(fake, Array.Empty<Tool>(), "test-model");

    [Test]
    public async Task RunAsync_NoToolCalls_EmitsTurnStartThenTurnEnd()
    {
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Hi back"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Api = "test", Provider = "fake", Model = "test",
                        Content = [new TextBlock("Hi back")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
        ]);

        var harness = CreateHarness(fake);

        var events = new List<HarnessEvent>();
        await foreach (var ev in harness.RunAsync("Hello"))
        {
            events.Add(ev);
        }

        await Assert.That(events.First()).IsTypeOf<TurnStartEvent>();
        await Assert.That(((TurnStartEvent)events.First()).Turn).IsEqualTo(1);
        await Assert.That(events.Last()).IsTypeOf<TurnEndEvent>();
        await Assert.That(events.OfType<AssistantTextDeltaEvent>().Count()).IsEqualTo(1);
        await Assert.That(harness.Messages.Count).IsEqualTo(2); // user + assistant
    }

    [Test]
    public async Task RunAsync_NoSteeringOrFollowUp_TerminatesAfterOneTurn()
    {
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("done"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Api = "test", Provider = "fake", Model = "test",
                        Content = [new TextBlock("done")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
        ]);

        var harness = CreateHarness(fake);
        var turnCount = 0;
        await foreach (var ev in harness.RunAsync("Hi"))
        {
            if (ev is TurnStartEvent) turnCount++;
        }

        await Assert.That(turnCount).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_ProviderError_SurfacesAsErrorTurnEnd_DoesNotThrow()
    {
        // Provider-level failures are terminal assistant messages
        // (StopReason=Error), not exceptions — the loop ends gracefully so
        // the session can persist the failure and route it to the UI.
        var fake = new FakePhiProvider(
        [
            new ProviderEvent[]
            {
                new ProviderErrorEvent("HTTP 500: server error"),
            },
        ]);

        var harness = CreateHarness(fake);

        var events = new List<HarnessEvent>();
        await foreach (var ev in harness.RunAsync("hi"))
        {
            events.Add(ev);
        }

        var turnEnd = events.OfType<TurnEndEvent>().Single();
        await Assert.That(turnEnd.FinalMessage.StopReason).IsEqualTo(StopReasons.Error);
        await Assert.That(turnEnd.FinalMessage.ErrorMessage).Contains("HTTP 500");

        // user + assistant(error) — the failure stays in history.
        await Assert.That(harness.Messages.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RunAsync_NonUserCancellation_Propagates()
    {
        // An OperationCanceledException whose token is NOT the run's
        // cancellation token (e.g. an HttpClient timeout) is a provider
        // failure, not a user interrupt — it must propagate to the caller
        // (Session records it as LastError) instead of being swallowed as
        // "interrupted".
        var harness = new Harness(
            new TimeoutStubProvider(), Array.Empty<Tool>(), "test-model");

        await Assert.That(async () =>
        {
            await foreach (var _ in harness.RunAsync("hi")) { }
        }).Throws<OperationCanceledException>();
    }

    /// <summary>Provider that dies mid-stream with a non-user cancellation.</summary>
    private sealed class TimeoutStubProvider : IPhiProvider
    {
        public void Dispose() { }

        public async IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
            string model,
            string system,
            IList<IAgentMessage> messages,
            IReadOnlyList<Tool> tools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            // Simulates HttpClient.Timeout: canceled by an internal token,
            // not by the caller's cancellationToken.
            throw new TaskCanceledException("The request timed out.");
#pragma warning disable CS0162 // unreachable — satisfies the async-iterator yield requirement
            yield break;
#pragma warning restore CS0162
        }
    }

    [Test]
    public async Task RunAsync_CancelSurfacesAsHarnessErrorEvent_DoesNotThrow()
    {
        // Pre-cancelled token + one queued event so FakePhiProvider yields
        // at least once before its ThrowIfCancellationRequested fires. The
        // harness should swallow the OCE, surface it as a HarnessErrorEvent,
        // and end the session normally — never re-throwing to the caller.
        var fake = new FakePhiProvider(
        [
            [new ProviderTextDeltaEvent("hel")],
        ]);
        var harness = CreateHarness(fake);

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

        await Assert.That(harness.Messages.OfType<UserMessage>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_CancelWithInterruptedTool_InsertsPlaceholderViaIntegration()
    {
        // Pre-seed the harness with a partial assistant message (as if the
        // model streamed a tool call then the user hit Esc mid-tool), then
        // run with a cancelled token. RunAsync's catch path should call
        // AppendInterruptedToolResults, leaving the message chain well-formed.
        var toolCall = new ToolCall("c1", "bash")
        {
            Arguments = JsonNode.Parse("""{"command":"ls"}""")!.AsObject(),
        };
        var fake = new FakePhiProvider(
        [
            [new ProviderTextDeltaEvent("x")],
        ]);
        var harness = new Harness(fake, Array.Empty<Tool>(), "test");
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
}
