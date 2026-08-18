using Avalonia.Controls;
using Avalonia.Layout;
using Material.Icons.Avalonia;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="ShellLayout"/>: pure-declarative chrome — verifies that the
/// sidebar + view host tree, the named controls, and the chrome styling all
/// match what <see cref="ShellView"/> assumes. These tests pin the layout
/// shape that XAML expresses, so a stray edit to <c>ShellLayout.axaml</c>
/// fails loudly rather than silently shifting a control into the wrong
/// grid row.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ShellLayoutTests
{
    [Test]
    public async Task Root_IsUserControl_AndContent_IsTwoColumnGrid()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        await Assert.That(layout).IsAssignableTo<UserControl>();
        var outer = layout.Content as Grid;
        await Assert.That(outer).IsNotNull();
        await Assert.That(outer!.ColumnDefinitions.Count).IsEqualTo(2);
        await Assert.That(outer.Children.Count).IsEqualTo(2);
    }

    [Test]
    public async Task LeftColumn_Is240pxBorder_HostingSixRowPane()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var outer = (Grid)layout.Content!;
        var leftBorder = (Border)outer.Children[0];
        await Assert.That(leftBorder.Width).IsEqualTo(240);
        await Assert.That(leftBorder.BorderThickness.Right).IsEqualTo(1);

        var pane = (Grid)leftBorder.Child!;
        await Assert.That(pane.RowDefinitions.Count).IsEqualTo(6);
    }

    [Test]
    public async Task RightColumn_IsTheViewHostContentControl()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var outer = (Grid)layout.Content!;
        await Assert.That(ReferenceEquals(outer.Children[1], layout.ViewHost)).IsTrue();
    }

    [Test]
    public async Task SessionsHeader_IsTwoColumnGrid_WithLabelAndToggleStack()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var pane = LeftPane(layout);
        var header = (Grid)pane.Children[2];
        await Assert.That(header.ColumnDefinitions.Count).IsEqualTo(2);

        var label = (TextBlock)header.Children[0];
        await Assert.That(label.Text).IsEqualTo("会话");

        var toggles = (StackPanel)header.Children[1];
        await Assert.That(toggles.Orientation).IsEqualTo(Orientation.Horizontal);
        await Assert.That(toggles.HorizontalAlignment).IsEqualTo(HorizontalAlignment.Right);
        await Assert.That(toggles.Children.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(toggles.Children[0], layout.ByDateButton)).IsTrue();
        await Assert.That(ReferenceEquals(toggles.Children[1], layout.ByWorkspaceButton)).IsTrue();
    }

    [Test]
    public async Task ToggleButtons_AreIconOnly_MaterialIcons()
    {
        // Toggle buttons must be icon-only — a MaterialIcon as Content, no
        // TextBlock label — so they fit the icon-only toggle row.
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        await Assert.That(layout.ByDateButton.Content).IsAssignableTo<MaterialIcon>();
        await Assert.That(layout.ByWorkspaceButton.Content).IsAssignableTo<MaterialIcon>();
    }

    [Test]
    public async Task Footer_IsProvidersButton_Alone()
    {
        // The model picker lives in PromptInputView's toolbar; the sidebar
        // footer only hosts the Providers button.
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var pane = LeftPane(layout);
        await Assert.That(ReferenceEquals(pane.Children[5], layout.ProvidersButton)).IsTrue();
    }

    [Test]
    public async Task Dividers_AreOnePixelTall_AtRowsOneAndFour()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var pane = LeftPane(layout);
        var topDivider = (Border)pane.Children[1];
        var bottomDivider = (Border)pane.Children[4];
        await Assert.That(topDivider.Height).IsEqualTo(1);
        await Assert.That(bottomDivider.Height).IsEqualTo(1);
    }

    [Test]
    public async Task SessionsList_OccupiesTheStarRow()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var pane = LeftPane(layout);
        await Assert.That(ReferenceEquals(pane.Children[3], layout.SessionsList)).IsTrue();
        // Row 3 is the star row (the only `*` row in the 6-row grid).
        await Assert.That(pane.RowDefinitions[3].Height).IsEqualTo(new GridLength(1, GridUnitType.Star));
    }

    [Test]
    public async Task NewChatButton_OccupiesTheTopRow()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var pane = LeftPane(layout);
        await Assert.That(ReferenceEquals(pane.Children[0], layout.NewChatButton)).IsTrue();
    }

    private static Grid LeftPane(ShellLayout layout) =>
        (Grid)((Border)((Grid)layout.Content!).Children[0]).Child!;
}