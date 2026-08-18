using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using MarkView.Avalonia;
using Material.Icons.Avalonia;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Avalonia;

/// <summary>
/// Avalonia <see cref="Application"/>. The application shell lives in
/// <c>PhiAvaloniaApp.axaml</c> (Fluent theme + MarkView theme via compiled
/// <c>StyleInclude</c>, which is AOT-safe), while the rest of the UI is
/// built in C# to mirror the repo's code-only style. The desktop head
/// installs a classic lifetime with a single main window; single-view
/// platforms (Android, browser) host the same shell control directly.
/// </summary>
public sealed partial class PhiAvaloniaApp : Application
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
        // Load the compiled PhiAvaloniaApp.axaml (SukiTheme + MarkView
        // MarkdownTheme via a compile-time-resolved StyleInclude). This is
        // the official AOT-safe pattern — the XAML is compiled into the
        // assembly and loaded by type, with no runtime XAML compilation.
        AvaloniaXamlLoader.Load(this);

        // Expose the AvaloniaTheme semantic brushes as app resources so XAML
        // chrome can reference them via {DynamicResource ControlBorder} etc.
        // Re-registered on every theme-variant change so the XAML chrome
        // (sidebar border, dividers, submit button) follows SukiUI's runtime
        // light/dark switching just like the C#-built components do.
        RegisterThemeResources();
        ActualThemeVariantChanged += (_, _) => RegisterThemeResources();

        // Material.Icons.Avalonia's MaterialIcon control is a templated
        // control; without its styles the icons render as empty boxes.
        // Must be added to the app styles (see the package README for 2.0+).
        // The XAML-derived ctor needs a service provider; build a root one.
        Styles.Add(new MaterialIconStyles(XamlIlRuntimeHelpers.CreateRootServiceProviderV3(null)));
        // Keep code blocks on the same portable mono chain as the rest of
        // the app (the MarkView theme defaults to Cascadia Code / Consolas).
        Styles.Add(new Style(x => x.OfType<Border>()
            .Class("markdown-code-block").Child().OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.FontFamilyProperty, AvaloniaTheme.MonoFontFamily),
            },
        });
        // TextMate syntax highlighting for fenced code blocks in every
        // MarkdownViewer (transcript + read-tool cards), switching between
        // DarkPlus / LightPlus automatically with the OS theme.
        MarkdownViewerDefaults.Extensions.AddTextMateHighlighting();

        // macOS: install the app menu BEFORE the platform builds it. The
        // Avalonia.Native application-menu exporter constructs itself during
        // AfterSetup (which runs after Initialize) and immediately reads
        // NativeMenu.GetMenu(Application) — if it's null it installs a
        // hardcoded default menu ("About Avalonia") that later SetMenu calls
        // can't replace. Setting it here guarantees our menu is the one used.
        if (OperatingSystem.IsMacOS())
            InstallMacAppMenu();
    }

    /// <summary>
    /// Registers the <see cref="AvaloniaTheme"/> semantic brushes as
    /// application-level resources so XAML chrome can reference them via
    /// <c>{DynamicResource ControlBorder}</c> etc. Re-run on every
    /// <see cref="Application.ActualThemeVariant"/> change (from
    /// <c>Initialize</c>) so the XAML chrome follows SukiUI's runtime
    /// light/dark switching. SukiUI's own theme dictionaries aren't
    /// reachable from <c>Application.Current</c>, so we bridge them through
    /// <see cref="AvaloniaTheme"/>'s SukiUI-mapped hex pairs instead.
    /// </summary>
    private void RegisterThemeResources()
    {
        Resources["TextPrimary"] = AvaloniaTheme.TextPrimary;
        Resources["TextSecondary"] = AvaloniaTheme.TextSecondary;
        Resources["Danger"] = AvaloniaTheme.Danger;
        Resources["DangerBackground"] = AvaloniaTheme.DangerBackground;
        Resources["Success"] = AvaloniaTheme.Success;
        Resources["ControlBorder"] = AvaloniaTheme.ControlBorder;
        Resources["ContainerBackground"] = AvaloniaTheme.ContainerBackground;
        Resources["Accent"] = AvaloniaTheme.Accent;
        Resources["AccentText"] = AvaloniaTheme.AccentText;
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
