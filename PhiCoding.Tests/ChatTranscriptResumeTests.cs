using PhiAgent;
using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using TextBlock = PhiAgent.TextBlock;
using DocumentFlow = XenoAtom.Terminal.UI.Controls.DocumentFlow;

namespace PhiCoding.Tests.Helpers;

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

        var flow = transcript.Visual as DocumentFlow;
        await Assert.That(flow).IsNotNull();
        await Assert.That(flow!.Items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AddError_RendersErrorMessage()
    {
        var transcript = new ChatTranscript();

        transcript.AddError("something broke");

        var flow = transcript.Visual as DocumentFlow;
        await Assert.That(flow).IsNotNull();
        await Assert.That(flow!.Items.Count).IsEqualTo(1);
    }

}
