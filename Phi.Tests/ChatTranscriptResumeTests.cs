using Phi.Agent;
using Phi.Tests.Helpers;
using Phi.Tui.Components;
using TextBlock = Phi.Agent.TextBlock;

namespace Phi.Tests;

[NotInParallel(TuiTestGroups.BindingManager)]
public class ChatTranscriptResumeTests
{
    [Test]
    public async Task ClearAndLoad_RendersUserAndAssistantMessages()
    {
        var session = new MockSession();
        var transcript = new ChatTranscript();
        transcript.Bind(session);

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
        var session = new MockSession();
        var transcript = new ChatTranscript();
        transcript.Bind(session);

        transcript.AddPersistentError("something broke");

        var flow = transcript.Flow;
        await Assert.That(flow).IsNotNull();
        await Assert.That(flow!.Items.Count).IsEqualTo(1);
    }

}
