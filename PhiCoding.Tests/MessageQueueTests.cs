using PhiAgent;

namespace PhiCoding.Tests;

public class MessageQueueTests
{
    private static UserMessage Msg(string text) => new() { Content = text };

    [Test]
    public async Task EnqueueSteering_IncreasesCount()
    {
        var q = new MessageQueue();

        await Assert.That(q.SteeringCount).IsEqualTo(0);
        q.EnqueueSteering(Msg("first"));
        q.EnqueueSteering(Msg("second"));
        await Assert.That(q.SteeringCount).IsEqualTo(2);
        await Assert.That(q.FollowUpCount).IsEqualTo(0);
    }

    [Test]
    public async Task EnqueueFollowUp_DoesNotAffectSteeringCount()
    {
        var q = new MessageQueue();
        q.EnqueueSteering(Msg("s"));
        q.EnqueueFollowUp(Msg("f"));

        await Assert.That(q.SteeringCount).IsEqualTo(1);
        await Assert.That(q.FollowUpCount).IsEqualTo(1);
    }

    [Test]
    public async Task DrainSteering_ReturnsAllInFifoOrder_ThenClears()
    {
        var q = new MessageQueue();
        q.EnqueueSteering(Msg("a"));
        q.EnqueueSteering(Msg("b"));
        q.EnqueueSteering(Msg("c"));

        var drained = q.DrainSteering();

        await Assert.That(drained.Select(m => m.Text)).IsEquivalentTo(["a", "b", "c"]);
        await Assert.That(q.SteeringCount).IsEqualTo(0);
    }

    [Test]
    public async Task DrainSteering_OnEmpty_ReturnsEmptyAndDoesNotThrow()
    {
        var q = new MessageQueue();

        var drained = q.DrainSteering();

        await Assert.That(drained).IsEmpty();
    }

    [Test]
    public async Task DrainFollowUp_OnlyDrainsFollowUpQueue()
    {
        var q = new MessageQueue();
        q.EnqueueSteering(Msg("s"));
        q.EnqueueFollowUp(Msg("f1"));
        q.EnqueueFollowUp(Msg("f2"));

        var drained = q.DrainFollowUp();

        await Assert.That(drained.Select(m => m.Text)).IsEquivalentTo(["f1", "f2"]);
        await Assert.That(q.SteeringCount).IsEqualTo(1); // steering untouched
    }

    [Test]
    public async Task RepeatedDrainSteering_AfterEmpty_IsIdempotent()
    {
        var q = new MessageQueue();
        q.EnqueueSteering(Msg("only"));

        var first = q.DrainSteering();
        var second = q.DrainSteering();

        await Assert.That(first.Select(m => m.Text)).IsEquivalentTo(["only"]);
        await Assert.That(second).IsEmpty();
    }

    [Test]
    public async Task Clear_EmptiesBothQueues()
    {
        var q = new MessageQueue();
        q.EnqueueSteering(Msg("s"));
        q.EnqueueFollowUp(Msg("f"));

        q.Clear();

        await Assert.That(q.SteeringCount).IsEqualTo(0);
        await Assert.That(q.FollowUpCount).IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentEnqueueAndDrain_PreservesAllMessages()
    {
        // Producer/consumer race: 4 producers enqueue 250 messages each into
        // steering; main thread repeatedly drains. After the producers join,
        // every message must have been delivered exactly once across all
        // drains (FIFO across a single producer is irrelevant — we only
        // verify count, since order between producers is undefined).
        var q = new MessageQueue();
        var producers = Enumerable.Range(0, 4).Select(id => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
            {
                q.EnqueueSteering(Msg($"p{id}-m{i}"));
            }
        })).ToList();

        var collected = new List<UserMessage>();
        while (producers.Any(p => !p.IsCompleted))
        {
            foreach (var m in q.DrainSteering()) collected.Add(m);
            await Task.Delay(1);
        }
        foreach (var m in q.DrainSteering()) collected.Add(m);

        await Task.WhenAll(producers);

        await Assert.That(collected.Count).IsEqualTo(1000);
        await Assert.That(q.SteeringCount).IsEqualTo(0);
    }
}
