using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Phi.Chat;
using Phi.Extensions.Host;
using Phi.Providers;
using SukiUI.Controls;

namespace Phi.Avalonia;

/// <summary>
/// The desktop main window: hosts the <see cref="ShellView"/> and wires
/// the window's Closed event to disposal. Built on <see cref="SukiWindow"/>
/// (Phase 1) so the app gets SukiUI's themed window chrome — a custom
/// title bar with the app logo — instead of the stock OS title bar.
/// </summary>
public sealed class MainWindow : SukiWindow
{
    private readonly ShellView _shell;

    public MainWindow(ActiveSession active, ProviderManager providers)
        : this(active, providers, null, null)
    {
    }

    public MainWindow(ActiveSession active, ProviderManager providers, Action<IUiSink>? onSinkBuilt)
        : this(active, providers, onSinkBuilt, null)
    {
    }

    public MainWindow(
        ActiveSession active,
        ProviderManager providers,
        Action<IUiSink>? onSinkBuilt,
        Func<IExtensionRenderers?>? renderersAccessor)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(providers);

        // SukiWindow draws its own title bar, so the title is shown on every
        // platform (the old macOS "keep title empty" hack was for the OS
        // title bar, which no longer renders).
        Title = "Phi";
        Width = 1024;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // Window icon (titlebar / taskbar). Loaded from the embedded
        // AvaloniaResource so it works under NativeAOT and on every
        // platform; macOS ignores window icons in the titlebar, so this
        // primarily affects Windows / Linux. The macOS dock icon comes
        // from the .app bundle's AppIcon.icns instead.
        // AssetLoader routes the avares:// URI to the embedded resource
        // (Bitmap(string) would treat it as a filesystem path).
        Icon = new WindowIcon(
            new Bitmap(AssetLoader.Open(new Uri("avares://Phi.Avalonia/Assets/phi.png"))));

        // App logo shown in the SukiWindow title bar, next to the title.
        LogoContent = new Image
        {
            Source = new Bitmap(AssetLoader.Open(
                new Uri("avares://Phi.Avalonia/Assets/phi.png"))),
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // No app menu in the title bar (Phase 1); the shell fills the window.
        IsMenuVisible = false;
        IsTitleBarVisible = true;

        _shell = new ShellView(active, providers, onSinkBuilt: onSinkBuilt, renderersAccessor: renderersAccessor);
        Content = _shell.Root;

        Closed += (_, _) => _shell.Dispose();
    }

    /// <summary>The shell, exposed for tests.</summary>
    internal ShellView Shell => _shell;
}
