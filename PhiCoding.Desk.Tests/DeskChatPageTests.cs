using Aprillz.MewUI;
using PhiCoding.Desk.Tests.Helpers;
using PhiCoding.Providers;

namespace PhiCoding.Desk.Tests;

/// <summary>
/// <see cref="DeskChatPage"/>: the chat page must lay out so the transcript
/// fills the middle region between the header (top) and the prompt input +
/// status bar (bottom). Regression: <c>DockPanel.LastChildFill</c> gave the
/// status bar all remaining space, collapsing the transcript to zero height.
/// </summary>
[NotInParallel(DeskTestGroups.Components)]
public class DeskChatPageTests
{
    private const double Width = 800;
    private const double Height = 600;

    private static DeskChatPage CreatePage()
    {
        MewTestHost.EnsureBackend();
        var session = new MockSession();
        var navigator = new FakeSessionNavigator(session);
        return new DeskChatPage(navigator, new ProviderManager(), session);
    }

    private static void Layout(DeskChatPage page)
    {
        page.Root.Measure(new Size(Width, Height));
        page.Root.Arrange(new Rect(0, 0, Width, Height));
    }

    [Test]
    public async Task Layout_TranscriptFillsMiddleRegion()
    {
        using var page = CreatePage();
        Layout(page);

        // Header (~40) + prompt input (~60) + status bar (~30) leave most of
        // the 600px height to the transcript.
        await Assert.That(page.TranscriptRoot.RenderSize.Height).IsGreaterThan(200);
    }

    [Test]
    public async Task Layout_PromptInputSitsAtBottom()
    {
        using var page = CreatePage();
        Layout(page);

        // The prompt input's bottom edge reaches the bottom of the page
        // (status bar sits below it, so its bottom is at ~Height - statusBar).
        await Assert.That(page.PromptInputRoot.Bounds.Bottom).IsLessThanOrEqualTo(Height);
    }

    [Test]
    public async Task Layout_TranscriptFitsWidth()
    {
        using var page = CreatePage();
        Layout(page);

        await Assert.That(page.TranscriptRoot.RenderSize.Width).IsGreaterThan(Width / 2);
    }
}