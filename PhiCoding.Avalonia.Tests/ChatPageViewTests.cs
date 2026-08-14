using PhiCoding.Avalonia.Components;
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

        var grid = (global::Avalonia.Controls.Grid)page.Root;
        // Transcript + prompt input, no header.
        await Assert.That(grid.RowDefinitions.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(grid.Children[0], page.Transcript.Root)).IsTrue();
        await Assert.That(ReferenceEquals(grid.Children[1], page.PromptInput.Root)).IsTrue();
    }

    [Test]
    public async Task PromptInput_HasSideMargins_MatchingTranscriptPadding()
    {
        var (_, page) = Create();

        var input = (global::Avalonia.Controls.Border)page.PromptInput.Root;
        // Left/right margins align with the transcript's 48px document padding.
        await Assert.That(input.Margin.Left).IsEqualTo(48);
        await Assert.That(input.Margin.Right).IsEqualTo(48);
    }
}
