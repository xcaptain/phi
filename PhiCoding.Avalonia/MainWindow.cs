using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Avalonia;

/// <summary>
/// The desktop main window: hosts the <see cref="ShellView"/> and wires
/// the window's Closed event to disposal.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly ShellView _shell;

    public MainWindow(ISessionNavigator navigator, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);

        // macOS shows the app name in the menu bar (via the process name)
        // and in the title bar of untitled windows, so the window title can
        // stay empty there for a cleaner look. Windows / Linux rely on the
        // window title for the title bar and taskbar, so keep it explicit.
        Title = OperatingSystem.IsMacOS() ? "" : "Phi";
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
            new Bitmap(AssetLoader.Open(new Uri("avares://PhiCoding.Avalonia/Assets/phi.png"))));

        _shell = new ShellView(navigator, providers);
        Content = _shell.Root;

        Closed += (_, _) => _shell.Dispose();
    }

    /// <summary>Releases the shell's subscriptions.</summary>
    public void Dispose() => _shell.Dispose();


    /// <summary>The shell, exposed for tests.</summary>
    internal ShellView Shell => _shell;
}
