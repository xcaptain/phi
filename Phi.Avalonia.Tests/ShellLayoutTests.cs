using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Material.Icons.Avalonia;
using SukiUI.Controls;

namespace Phi.Avalonia.Tests;

/// <summary>
/// <see cref="ShellLayout"/>: pure-declarative chrome built on SukiUI's
/// <see cref="SukiSideMenu"/> — the sessions browser lives in the side
/// menu's HeaderContent, the ViewHost in its custom Content. These tests
/// pin the layout shape that XAML expresses, so a stray edit to
/// <c>ShellLayout.axaml</c> fails loudly rather than silently shifting a
/// control into the wrong slot.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class ShellLayoutTests
{
    [Test]
    public async Task Root_IsUserControl_HostingSukiSideMenu()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        await Assert.That(layout).IsAssignableTo<UserControl>();
        await Assert.That(layout.Content).IsAssignableTo<SukiSideMenu>();
    }

    [Test]
    public async Task SideMenu_Has240pxOpenPane_AndCustomContent()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var menu = (SukiSideMenu)layout.Content!;

        await Assert.That(menu.OpenPaneLength).IsEqualTo(240);
        // UseCustomContent lets ShellView drive the ViewHost directly
        // instead of SukiSideMenu's selected-item navigation model.
        await Assert.That(menu.UseCustomContent).IsTrue();
        // Search filters menu-item headers — we have no menu items, so it's
        // disabled (the sessions list scrolls on its own).
        await Assert.That(menu.IsSearchEnabled).IsFalse();
        // With no menu items a collapsed pane is an empty strip.
        await Assert.That(menu.SidebarToggleEnabled).IsFalse();
    }

    [Test]
    public async Task ViewHost_IsTheCustomContent()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var menu = (SukiSideMenu)layout.Content!;
        await Assert.That(ReferenceEquals(menu.Content, layout.ViewHost)).IsTrue();
    }

    [Test]
    public async Task SessionsBrowser_HostsFiveRowPane()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var pane = LeftPane(layout);
        // HeaderContent grid: [New Chat, divider, sessions header, sessions list,
        // Providers]. MaxHeight is bound to the side menu height so the grid
        // can never overflow the pane.
        await Assert.That(pane.RowDefinitions.Count).IsEqualTo(5);
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
    public async Task ProvidersButton_IsPinnedAtPaneBottom()
    {
        // The model picker lives in PromptInputView's toolbar; the sidebar
        // footer only hosts the Providers button. It sits in the bounded
        // HeaderContent grid's bottom row so it stays visible even in a
        // short window (rather than SukiSideMenu's FooterContent, which the
        // overflowing sessions list used to push off-screen).
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var pane = LeftPane(layout);
        await Assert.That(ReferenceEquals(pane.Children[4], layout.ProvidersButton)).IsTrue();
    }

    [Test]
    public async Task HeaderContent_IsCappedToSideMenuHeight()
    {
        // Regression: in a short window the sessions browser must shrink
        // (scrolling internally) instead of overflowing the pane and pushing
        // the pinned Providers row off-screen. MaxHeight is bound to the side
        // menu's height, which only resolves once the layout is shown.
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var window = new Window { Width = 400, Height = 260, Content = layout };
        window.Show();
        try
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var pane = LeftPane(layout);
            await Assert.That(pane.MaxHeight).IsGreaterThan(0);

            // The Providers row sits within the pane's bounded height, so it
            // stays on-screen instead of being pushed below it.
            var providers = layout.ProvidersButton;
            var bottom = providers.TranslatePoint(new global::Avalonia.Point(0, providers.Bounds.Height), layout)?.Y;
            await Assert.That(bottom).IsNotNull();
            await Assert.That(bottom!.Value).IsLessThanOrEqualTo(layout.Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public async Task Divider_IsOnePixelTall_UnderNewChat()
    {
        AvaloniaTestHost.EnsureInitialized();
        var layout = new ShellLayout();
        var pane = LeftPane(layout);
        var topDivider = (Border)pane.Children[1];
        await Assert.That(topDivider.Height).IsEqualTo(1);
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
        (Grid)((SukiSideMenu)layout.Content!).HeaderContent!;
}