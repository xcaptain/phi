using PhiAgent;
using PhiCoding.Tui.Components;
using TextBlock = PhiAgent.TextBlock;

namespace PhiCoding.Tests;

[NotInParallel(TuiTestGroups.BindingManager)]
public class ChatTranscriptResumeTests
{
    [Test]
    public async Task ClearAndLoad_RendersUserAndAssistantMessages()
    {
        var transcript = new ChatTranscript();

        var messages = new IAgentMessage[]
        {
            new UserMessage { Content = "hello" },
            new AssistantMessage
            {
                Content = [new TextBlock("world")],
                StopReason = StopReasons.Stop,
            },
        };

        transcript.ClearAndLoad(messages);

        var flow = transcript.Flow;
        await Assert.That(flow).IsNotNull();
        await Assert.That(flow!.Items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AddPersistentError_RendersErrorMessage()
    {
        var transcript = new ChatTranscript();

        transcript.AddPersistentError("something broke");

        var flow = transcript.Flow;
        await Assert.That(flow).IsNotNull();
        await Assert.That(flow!.Items.Count).IsEqualTo(1);
    }

}
