using Avalonia.Controls;
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

        Title = "Phi";
        Width = 1024;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _shell = new ShellView(navigator, providers);
        Content = _shell.Root;

        Closed += (_, _) => _shell.Dispose();
    }

    /// <summary>Releases the shell's subscriptions.</summary>
    public void Dispose() => _shell.Dispose();


    /// <summary>The shell, exposed for tests.</summary>
    internal ShellView Shell => _shell;
}
