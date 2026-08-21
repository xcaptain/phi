using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Phi.Avalonia.Components;
using Phi.Providers;
using global::Avalonia;

namespace Phi.Avalonia.Tests;

/// <summary>
/// Verifies the XAML chrome's <c>{DynamicResource …}</c> aliases resolve
/// AND follow runtime theme switching. SukiUI's own theme dictionaries
/// aren't reachable from <c>Application.Current</c>, so <see cref="AvaloniaTheme"/>
/// bridges them via SukiUI-mapped hex pairs, re-registered on every
/// <see cref="Application.ActualThemeVariant"/> change. These tests pin
/// both the light resolution and the light→dark update.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class SukiChromeResolutionTests
{
    [Test]
    public async Task ShellLayout_DividersFollowThemeSwitch()
    {
        AvaloniaTestHost.EnsureInitialized();
        var app = Application.Current!;
        app.RequestedThemeVariant = ThemeVariant.Light;

        var layout = new ShellLayout();
        var w = Show(layout);
        try
        {
            // The sessions browser pane lives in SukiSideMenu.HeaderContent;
            // the top divider is a 1px Border using {DynamicResource ControlBorder}.
            var sideMenu = (SukiUI.Controls.SukiSideMenu)layout.Content!;
            var pane = (Grid)sideMenu.HeaderContent!;
            var topDivider = (Border)pane.Children[1];

            var light = topDivider.Background as SolidColorBrush;
            await Assert.That(light).IsNotNull();
            // Light ControlBorder = SukiControlBorderBrush light #CECECE.
            await Assert.That(light!.Color).IsEqualTo(Color.Parse("#CECECE"));

            // Flip to dark: the re-registration must repaint the chrome.
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            var dark = topDivider.Background as SolidColorBrush;
            await Assert.That(dark).IsNotNull();
            // Dark ControlBorder = SukiControlBorderBrush dark #606060.
            await Assert.That(dark!.Color).IsEqualTo(Color.Parse("#606060"));
        }
        finally
        {
            w.Close();
        }
    }

    [Test]
    public async Task PromptInputLayout_SubmitButton_ResolvesAccent()
    {
        AvaloniaTestHost.EnsureInitialized();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        var layout = new PromptInputLayout();
        var w = Show(layout);
        try
        {
            var brush = layout.SubmitButton.Background as SolidColorBrush;
            await Assert.That(brush).IsNotNull();
            // Accent = SukiPrimaryColor (Blue theme) #0A59F7.
            await Assert.That(brush!.Color).IsEqualTo(Color.Parse("#0A59F7"));
        }
        finally
        {
            w.Close();
        }
    }

    [Test]
    public async Task ProviderRowView_Card_ResolvesContainerBackgroundAndBorder()
    {
        AvaloniaTestHost.EnsureInitialized();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        var providers = new ProviderManager(credentials: new EmptyStore());
        var row = new ProviderRowView(providers.Providers[0], providers);
        var w = Show(row);
        try
        {
            var card = (Border)row.Content!;

            var bg = card.Background as SolidColorBrush;
            await Assert.That(bg).IsNotNull();
            // ContainerBackground = SukiCardBackground light = White.
            await Assert.That(bg!.Color).IsEqualTo(Colors.White);

            var border = card.BorderBrush as SolidColorBrush;
            await Assert.That(border).IsNotNull();
            await Assert.That(border!.Color).IsEqualTo(Color.Parse("#CECECE"));
        }
        finally
        {
            w.Close();
        }
    }

    private static Window Show(Control content)
    {
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = content,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private sealed class EmptyStore : ICredentialStore
    {
        public string? Get(string name) => null;
        public void Set(string name, string value) { }
        public void Delete(string name) { }
        public bool Has(string name) => false;
    }
}
