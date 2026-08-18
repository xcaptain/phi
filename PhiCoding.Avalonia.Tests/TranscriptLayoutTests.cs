using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using PhiCoding.Avalonia.Components;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="TranscriptLayout"/>: pure-declarative chat-transcript
/// chrome — a scrolling view with document-style reading margins around
/// a named <c>LinesPanel</c> slot that TranscriptView fills dynamically
/// as the projector emits lines.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class TranscriptLayoutTests
{
    [Test]
    public async Task Root_IsUserControl_WrappingScrollViewer()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new TranscriptLayout();
        await Assert.That(layout).IsAssignableTo<UserControl>();
        await Assert.That(layout.Content).IsAssignableTo<ScrollViewer>();
    }

    [Test]
    public async Task ScrollViewer_HasDocumentStylePadding()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new TranscriptLayout();
        var scroll = (ScrollViewer)layout.Content!;
        await Assert.That(scroll.Padding.Left).IsEqualTo(48);
        await Assert.That(scroll.Padding.Right).IsEqualTo(48);
        await Assert.That(scroll.Padding.Top).IsGreaterThan(0);
        await Assert.That(scroll.Padding.Bottom).IsGreaterThan(0);
    }

    [Test]
    public async Task ScrollViewer_HasAutoVerticalScrollBar()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new TranscriptLayout();
        var scroll = (ScrollViewer)layout.Content!;
        // The transcript grows over a chat session; auto-scroll only when
        // the content overflows the viewport.
        await Assert.That(scroll.VerticalScrollBarVisibility).IsEqualTo(ScrollBarVisibility.Auto);
    }

    [Test]
    public async Task LinesPanel_IsEmptyStackPanel_Slot()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new TranscriptLayout();
        var scroll = (ScrollViewer)layout.Content!;
        var panel = (StackPanel)scroll.Content!;
        await Assert.That(layout.LinesPanel).IsSameReferenceAs(panel);
        await Assert.That(panel.Children.Count).IsEqualTo(0);
        // 8 px gap between lines + a small top margin so the first line
        // doesn't sit flush against the scroll viewer's top edge.
        await Assert.That(panel.Spacing).IsEqualTo(8);
        await Assert.That(panel.Margin.Top).IsEqualTo(4);
    }
}