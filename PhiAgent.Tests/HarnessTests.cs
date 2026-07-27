using PhiAgent;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiAgent.Tests;

public class HarnessTests
{
    private static Harness CreateHarness(FakePhiProvider fake) =>
        new(fake, Array.Empty<IHarnessTool>(), "test-model");

    [Test]
    public async Task RunAsync_NoToolCalls_EmitsTurnStartThenTurnEnd()
    {
        var fake = new FakePhiProvider(new[]
        {
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
        });

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
        var fake = new FakePhiProvider(new[]
        {
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
        });

        var harness = CreateHarness(fake);
        var turnCount = 0;
        await foreach (var ev in harness.RunAsync("Hi"))
        {
            if (ev is TurnStartEvent) turnCount++;
        }

        await Assert.That(turnCount).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_SteeringMessages_RunAnotherTurnWithInjectedMessage()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("First"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Content = [new TextBlock("First")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Got steering"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Content = [new TextBlock("Got steering")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
        });

        var harness = CreateHarness(fake);

        var steeringFired = false;
        Func<IReadOnlyList<IAgentMessage>> getSteering = () =>
        {
            if (steeringFired) return [];
            steeringFired = true;
            return [new UserMessage { Content = "Actually do this" }];
        };

        var turnStarts = new List<TurnStartEvent>();
        await foreach (var ev in harness.RunAsync("first prompt", getSteeringMessages: getSteering))
        {
            if (ev is TurnStartEvent ts) turnStarts.Add(ts);
        }

        await Assert.That(turnStarts.Count()).IsEqualTo(2);
        await Assert.That(turnStarts[0].Turn).IsEqualTo(1);
        await Assert.That(turnStarts[1].Turn).IsEqualTo(2);

        // Messages: user1, assistant1, user2(steering), assistant2 = 4
        await Assert.That(harness.Messages.Count).IsEqualTo(4);
        await Assert.That(harness.Messages.OfType<UserMessage>().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task RunAsync_FollowUpMessages_AlsoTriggersAnotherTurn()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Done turn 1"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Content = [new TextBlock("Done turn 1")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("Done turn 2"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Content = [new TextBlock("Done turn 2")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
        });

        var harness = CreateHarness(fake);

        var followUpFired = false;
        Func<IReadOnlyList<IAgentMessage>> getFollowUp = () =>
        {
            if (followUpFired) return [];
            followUpFired = true;
            return [new UserMessage { Content = "follow up" }];
        };

        var turnStarts = new List<TurnStartEvent>();
        await foreach (var ev in harness.RunAsync("first", getFollowUpMessages: getFollowUp))
        {
            if (ev is TurnStartEvent ts) turnStarts.Add(ts);
        }

        await Assert.That(turnStarts.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task RunAsync_EmptySteering_DoesNotTriggerAnotherTurn()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderTextDeltaEvent("done"),
                new ProviderResponseEndEvent(
                    new AssistantMessage
                    {
                        Content = [new TextBlock("done")],
                        StopReason = StopReasons.Stop,
                    },
                    StopReasons.Stop),
            },
        });

        var harness = CreateHarness(fake);
        Func<IReadOnlyList<IAgentMessage>> getSteering = () => [];

        var turnCount = 0;
        await foreach (var ev in harness.RunAsync("Hi", getSteeringMessages: getSteering))
        {
            if (ev is TurnStartEvent) turnCount++;
        }

        await Assert.That(turnCount).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_ProviderErrorPropagates_ThroughOuterLoop()
    {
        var fake = new FakePhiProvider(new[]
        {
            new ProviderEvent[]
            {
                new ProviderErrorEvent("HTTP 500: server error"),
            },
        });

        var harness = CreateHarness(fake);

        var ex = await Assert.That(async () =>
        {
            await foreach (var _ in harness.RunAsync("hi")) { }
        }).Throws<InvalidOperationException>();

        await Assert.That(ex!.Message).Contains("HTTP 500");
    }
}