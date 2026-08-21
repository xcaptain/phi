using Avalonia.Controls;

namespace Phi.Avalonia.Tests;

/// <summary>
/// <see cref="ChatPageLayout"/>: pure-declarative chat-page chrome —
/// verifies that the two-row grid + named ContentControl slots match
/// what <see cref="ChatPageView"/> wires up. These tests pin the layout
/// shape that XAML expresses, so a stray edit to
/// <c>ChatPageLayout.axaml</c> fails loudly rather than silently moving
/// the prompt input out of the bottom row.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ChatPageLayoutTests
{
    [Test]
    public async Task Root_IsUserControl_WithGridContent()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ChatPageLayout();
        await Assert.That(layout).IsAssignableTo<UserControl>();
        await Assert.That(layout.Content).IsAssignableTo<Grid>();
    }

    [Test]
    public async Task Grid_HasTwoRows_StarTranscriptThenAutoInput()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ChatPageLayout();
        var grid = (Grid)layout.Content!;

        await Assert.That(grid.RowDefinitions.Count).IsEqualTo(2);
        // Row 0 (transcript) is the star row, fills available space.
        await Assert.That(grid.RowDefinitions[0].Height).IsEqualTo(new GridLength(1, GridUnitType.Star));
        // Row 1 (prompt input) is auto-sized to its content.
        await Assert.That(grid.RowDefinitions[1].Height).IsEqualTo(GridLength.Auto);
    }

    [Test]
    public async Task Slots_AreTwoContentControls_PinnedToTheirRows()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ChatPageLayout();
        var grid = (Grid)layout.Content!;

        await Assert.That(grid.Children.Count).IsEqualTo(2);

        var transcriptSlot = (ContentControl)grid.Children[0];
        var promptSlot = (ContentControl)grid.Children[1];
        await Assert.That(ReferenceEquals(transcriptSlot, layout.TranscriptHost)).IsTrue();
        await Assert.That(ReferenceEquals(promptSlot, layout.PromptInputHost)).IsTrue();

        await Assert.That(Grid.GetRow(transcriptSlot)).IsEqualTo(0);
        await Assert.That(Grid.GetRow(promptSlot)).IsEqualTo(1);
    }

    [Test]
    public async Task Slots_StartEmpty_ContentIsWiredByChatPageView()
    {
        // The layout is pure chrome — the controller (ChatPageView) injects
        // the live transcript / prompt input. Until then, the slots are
        // empty ContentControls.
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ChatPageLayout();
        await Assert.That(layout.TranscriptHost.Content).IsNull();
        await Assert.That(layout.PromptInputHost.Content).IsNull();
    }
}
