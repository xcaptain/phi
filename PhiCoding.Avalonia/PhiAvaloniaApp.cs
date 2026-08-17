using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using ColorTextBlock.Avalonia;
using Material.Icons.Avalonia;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Avalonia;

/// <summary>
/// Code-only Avalonia <see cref="Application"/>. The shell is built
/// entirely in C# (no XAML) to mirror the rest of the repo's style. The
/// desktop head installs a classic lifetime with a single main window;
/// single-view platforms (Android, browser) host the same shell control
/// directly.
/// </summary>
public sealed class PhiAvaloniaApp : Application
{
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;

    /// <summary>
    /// Parameterless ctor for the headless test host and design-time
    /// tooling. The real app is constructed with a navigator + provider
    /// manager via <see cref="PhiAvaloniaApp(ISessionNavigator, ProviderManager)"/>.
    /// </summary>
    public PhiAvaloniaApp()
    {
        Name = "Phi";
        _navigator = null!;
        _providers = null!;
    }

    public PhiAvaloniaApp(ISessionNavigator navigator, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        // Drives the macOS process name, which is what AppKit shows as the
        // first menu title (next to the Apple logo) in the menu bar. The
        // base default is "Avalonia Application".
        Name = "Phi";
        _navigator = navigator;
        _providers = providers;
    }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        // Material.Icons.Avalonia's MaterialIcon control is a templated
        // control; without its styles the icons render as empty boxes.
        // Must be added to the app styles (see the package README for 2.0+).
        // The XAML-derived ctor needs a service provider; build a root one.
        Styles.Add(new MaterialIconStyles(XamlIlRuntimeHelpers.CreateRootServiceProviderV3(null)));
        // Markdown.Avalonia's ColorTextBlock picks its monospace font by
        // asking the FontManager for any system font whose name contains
        // "menlo" / "monaco" / "consolas" / …; on macOS that heuristic can
        // pick Consolas (e.g. installed via Office) and crash with
        // "Could not create glyphTypeface" when the chosen family lacks
        // required glyphs. Force the same portable fallback chain we use
        // everywhere else so code blocks always resolve to a usable font.
        Styles.Add(new Style(x => x.OfType<CCode>())
        {
            Setters =
            {
                new Setter(CCode.MonospaceFontFamilyProperty, AvaloniaTheme.MonoFontFamily),
            },
        });
        // Follow the OS light/dark preference; FluentTheme resolves the
        // effective variant from the platform when Default is requested.
        RequestedThemeVariant = ThemeVariant.Default;

        // macOS: install the app menu BEFORE the platform builds it. The
        // Avalonia.Native application-menu exporter constructs itself during
        // AfterSetup (which runs after Initialize) and immediately reads
        // NativeMenu.GetMenu(Application) — if it's null it installs a
        // hardcoded default menu ("About Avalonia") that later SetMenu calls
        // can't replace. Setting it here guarantees our menu is the one used.
        if (OperatingSystem.IsMacOS())
            InstallMacAppMenu();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_navigator is null)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow(_navigator, _providers);
                break;
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new ShellView(_navigator, _providers).Root;
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Sets the macOS application menu. Only the first item is ours ("About
    /// Phi", which opens <see cref="ShowAboutDialogAsync"/>); the platform
    /// appends the standard Services / Hide / Quit items afterwards.
    /// </summary>
    private void InstallMacAppMenu()
    {
        var about = new NativeMenuItem("About Phi");
        about.Click += async (_, _) => await ShowAboutDialogAsync();
        NativeMenu.SetMenu(this, [about]);
    }

    /// <summary>
    /// Small modal About window: app icon, name, a one-line description, and
    /// the author's contact info.
    /// </summary>
    private async Task ShowAboutDialogAsync()
    {
        var about = new Window
        {
            Title = "About Phi",
            Width = 340,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new Image
        {
            Source = new Bitmap(AssetLoader.Open(
                new Uri("avares://PhiCoding.Avalonia/Assets/phi.png"))),
            Width = 64,
            Height = 64,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Phi",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = "A minimal coding agent for your terminal and desktop.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = AvaloniaTheme.TextSecondary,
        });
        content.Children.Add(new TextBlock
        {
            Text = "By Joey Xie · joey.xf@gmail.com",
            TextWrapping = TextWrapping.Wrap,
            Foreground = AvaloniaTheme.TextSecondary,
            FontSize = 12,
        });
        about.Content = new Border
        {
            Child = content,
            Padding = new Thickness(24),
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } main })
            await about.ShowDialog(main);
        else
            about.Show();
    }
}
