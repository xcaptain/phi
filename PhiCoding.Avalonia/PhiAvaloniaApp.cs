using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
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
        _navigator = null!;
        _providers = null!;
    }

    public PhiAvaloniaApp(ISessionNavigator navigator, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
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
        // Follow the OS light/dark preference; FluentTheme resolves the
        // effective variant from the platform when Default is requested.
        RequestedThemeVariant = ThemeVariant.Default;
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
}
