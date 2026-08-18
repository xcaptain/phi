using PhiCoding.Avalonia.Tests.Helpers;
using PhiCoding.Providers;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="ChatPageView"/>: the chat page is transcript + prompt input,
/// with no header row. The input box carries the same side padding as the
/// transcript so the composition reads as one aligned document column.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ChatPageViewTests
{
    private static (MockSession session, ChatPageView page) Create()
    {
        AvaloniaTestHost.EnsureInitialized();
        var session = new MockSession();
        var navigator = new FakeSessionNavigator(session);
        var page = new ChatPageView(navigator, new ProviderManager(), session);
        return (session, page);
    }

    [Test]
    public async Task Layout_HasNoHeaderRow_TwoRowsOnly()
    {
        var (_, page) = Create();

        // page.Root is the ChatPageLayout UserControl; walk into the
        // two-row Grid, then check that each named slot holds the live
        // transcript / prompt input control.
        var grid = (global::Avalonia.Controls.Grid)((global::Avalonia.Controls.ContentControl)page.Root).Content!;
        await Assert.That(grid.RowDefinitions.Count).IsEqualTo(2);

        var transcriptSlot = (global::Avalonia.Controls.ContentControl)grid.Children[0];
        var promptSlot = (global::Avalonia.Controls.ContentControl)grid.Children[1];
        await Assert.That(ReferenceEquals(transcriptSlot.Content, page.Transcript.Root)).IsTrue();
        await Assert.That(ReferenceEquals(promptSlot.Content, page.PromptInput.Root)).IsTrue();
    }

    [Test]
    public async Task PromptInput_HasSideMargins_MatchingTranscriptPadding()
    {
        var (_, page) = Create();

        // page.PromptInput.Root is now the PromptInputLayout UserControl;
        // walk into its Content (the rounded Border).
        var layout = (global::Avalonia.Controls.ContentControl)page.PromptInput.Root;
        var input = (global::Avalonia.Controls.Border)layout.Content!;
        // Left/right margins align with the transcript's 48px document padding.
        await Assert.That(input.Margin.Left).IsEqualTo(48);
        await Assert.That(input.Margin.Right).IsEqualTo(48);
    }
}
