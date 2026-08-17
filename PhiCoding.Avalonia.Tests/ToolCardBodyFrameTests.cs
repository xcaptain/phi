using Avalonia;
using Avalonia.Controls;
using PhiCoding.Avalonia.Components.ToolCards;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="ToolCardBodyFrame"/>: wraps any tool card's detail content
/// in a scrollable Border with a hard max-height cap. The frame is what
/// keeps long <c>read</c> / <c>bash</c> output from stretching the
/// transcript.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ToolCardBodyFrameTests
{
    [Test]
    public async Task Constructor_WrapsContentInScrollViewer()
    {
        AvaloniaTestHost.EnsureInitialized();
        var content = new TextBlock { Text = "hello" };

        var frame = new ToolCardBodyFrame(content);

        await Assert.That(frame.Child).IsAssignableFrom<ScrollViewer>();
    }

    [Test]
    public async Task Constructor_SetsDefaultMaxHeight()
    {
        AvaloniaTestHost.EnsureInitialized();

        var frame = new ToolCardBodyFrame(new TextBlock { Text = "x" });

        await Assert.That(frame.MaxHeight).IsEqualTo(ToolCardBodyFrame.DefaultMaxHeight);
        await Assert.That(frame.MaxHeight).IsEqualTo(400);
    }

    [Test]
    public async Task Constructor_HonorsCustomMaxHeight()
    {
        AvaloniaTestHost.EnsureInitialized();

        var frame = new ToolCardBodyFrame(new TextBlock { Text = "x" }, maxHeight: 250);

        await Assert.That(frame.MaxHeight).IsEqualTo(250);
    }

    [Test]
    public async Task Constructor_AppliesConsistentChrome()
    {
        AvaloniaTestHost.EnsureInitialized();

        var frame = new ToolCardBodyFrame(new TextBlock { Text = "x" });

        await Assert.That(frame.Background).IsEqualTo(AvaloniaTheme.ContainerBackground);
        await Assert.That(frame.BorderBrush).IsEqualTo(AvaloniaTheme.ControlBorder);
        await Assert.That(frame.CornerRadius).IsEqualTo(new CornerRadius(6));
        await Assert.That(frame.Padding.Left).IsGreaterThan(0);
    }

    [Test]
    public async Task Constructor_NullContent_Throws()
    {
        AvaloniaTestHost.EnsureInitialized();

        await Assert.That(() => new ToolCardBodyFrame(null!))
            .Throws<ArgumentNullException>();
    }
}
