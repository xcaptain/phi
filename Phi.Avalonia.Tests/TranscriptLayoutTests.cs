using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Phi.Avalonia.Components;

namespace Phi.Avalonia.Tests;

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
    public async Task ScrollViewer_HasVerticalPaddingOnly()
    {
        // Horizontal breathing room comes from the 1:8:1 reading-column
        // grid, not the ScrollViewer's padding — the ScrollViewer only
        // keeps vertical padding.
        AvaloniaTestHost.EnsureInitialized();
        var layout = new TranscriptLayout();
        var scroll = (ScrollViewer)layout.Content!;
        await Assert.That(scroll.Padding.Left).IsEqualTo(0);
        await Assert.That(scroll.Padding.Right).IsEqualTo(0);
        await Assert.That(scroll.Padding.Top).IsGreaterThan(0);
        await Assert.That(scroll.Padding.Bottom).IsGreaterThan(0);
    }

    [Test]
    public async Task ReadingColumn_IsOneEightOne_Grid()
    {
        // The reading column is a 1:8:1 grid: content = 80% of the window
        // width, side margins = 10% each, so the margins scale with the
        // window width.
        AvaloniaTestHost.EnsureInitialized();
        var layout = new TranscriptLayout();
        var scroll = (ScrollViewer)layout.Content!;
        var grid = (Grid)scroll.Content!;
        await Assert.That(grid.ColumnDefinitions.Count).IsEqualTo(3);
        await Assert.That(grid.ColumnDefinitions[0].Width).IsEqualTo(new GridLength(1, GridUnitType.Star));
        await Assert.That(grid.ColumnDefinitions[1].Width).IsEqualTo(new GridLength(8, GridUnitType.Star));
        await Assert.That(grid.ColumnDefinitions[2].Width).IsEqualTo(new GridLength(1, GridUnitType.Star));
        // The LinesPanel lives in the center (8*) column.
        await Assert.That(Grid.GetColumn(layout.LinesPanel)).IsEqualTo(1);
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
        var grid = (Grid)scroll.Content!;
        var panel = (StackPanel)grid.Children[0];
        await Assert.That(layout.LinesPanel).IsSameReferenceAs(panel);
        await Assert.That(panel.Children.Count).IsEqualTo(0);
        // 8 px gap between lines + a small top margin so the first line
        // doesn't sit flush against the scroll viewer's top edge.
        await Assert.That(panel.Spacing).IsEqualTo(8);
        await Assert.That(panel.Margin.Top).IsEqualTo(4);
    }
}