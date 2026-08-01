using PhiAgent;

namespace PhiCoding.Tests;

public class CompactionPlannerTests
{
    private static List<IAgentMessage> Fill(int userCount, int assistantTurnsPerUser = 1)
    {
        var list = new List<IAgentMessage>();
        for (var i = 0; i < userCount; i++)
        {
            list.Add(new UserMessage { Content = $"user {i}" });
            for (var j = 0; j < assistantTurnsPerUser; j++)
                list.Add(new AssistantMessage
                {
                    Content = [new TextBlock(new string('x', 200))],
                    StopReason = StopReasons.Stop,
                });
        }
        return list;
    }

    [Test]
    public async Task Build_ShortHistory_NoPlan()
    {
        // Less than 2 messages → no plan possible.
        var plan = CompactionPlanner.Build(
        [
            new UserMessage { Content = "u" },
        ], keepRecentTokens: 100_000);
        await Assert.That(plan).IsNull();
    }

    [Test]
    public async Task Build_LongHistory_KeepsRecentSuffix()
    {
        var history = Fill(userCount: 20); // ~20 user + 20 assistant, each assistant ~50 tokens
        // Force the planner to keep just a small slice of recent tokens.
        var plan = CompactionPlanner.Build(history, keepRecentTokens: 50);
        await Assert.That(plan).IsNotNull();
        await Assert.That(plan!.MessagesToSummarize.Count).IsGreaterThan(0);
        // The very last kept message must be AssistantMessage (history
        // alternates user/assistant, last item is assistant).
        await Assert.That(plan.KeptMessages.Count).IsGreaterThan(0);
        await Assert.That(plan.KeptMessages.Last()).IsTypeOf<AssistantMessage>();
    }

    [Test]
    public async Task Build_AdjustsToNextUserMessageBoundary()
    {
        var history = new List<IAgentMessage>
        {
            new UserMessage { Content = "u0" },
            new AssistantMessage { Content = [new TextBlock(new string('a', 1000))], StopReason = StopReasons.Stop },
            new AssistantMessage { Content = [new TextBlock(new string('b', 1000))], StopReason = StopReasons.Stop },
            new UserMessage { Content = "u1" },
            new AssistantMessage { Content = [new TextBlock("tail")], StopReason = StopReasons.Stop },
        };
        // keepRecentTokens=50 → tail assistant is in the recent cutoff.
        // The planner must snap forward to the nearest user message ("u1").
        var plan = CompactionPlanner.Build(history, keepRecentTokens: 50);
        await Assert.That(plan).IsNotNull();
        // First kept must be a user message.
        await Assert.That(plan!.KeptMessages[0]).IsTypeOf<UserMessage>();
    }

    [Test]
    public async Task Build_TinyKeepRecent_StillProducesValidSplit()
    {
        // History [user0, assistant0] — keep 1 token. Walk from end:
        // assistant ~= 5 tokens > 1 → candidate=1. AdjustToBoundary(1):
        // assistant is not user; no next user; find first non-toolResult
        // from candidate=1 → assistant at index 1 → firstKept=1.
        var history = new List<IAgentMessage>
        {
            new UserMessage { Content = "u0" },
            new AssistantMessage { Content = [new TextBlock("a0")], StopReason = StopReasons.Stop },
        };
        var plan = CompactionPlanner.Build(history, keepRecentTokens: 1);
        await Assert.That(plan).IsNotNull();
        await Assert.That(plan!.MessagesToSummarize.Count).IsEqualTo(1);
        await Assert.That(plan.KeptMessages.Count).IsEqualTo(1);
        await Assert.That(plan.MessagesToSummarize[0]).IsTypeOf<UserMessage>();
        await Assert.That(plan.KeptMessages[0]).IsTypeOf<AssistantMessage>();
    }
}