using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Desk;

/// <summary>
/// Desktop shell. Unlike the TUI (a single full-screen chat page), the
/// desktop uses a two-pane layout mirroring the MewUI gallery:
/// <list type="bullet">
/// <item>left: a collapsible <see cref="NavigationView"/> pane holding the
/// chat entry, the recent-sessions list, and footer entries for Models and
/// Providers;</item>
/// <item>right: the selected view — the live chat page for the current
/// session, the model settings page, or the provider settings page.</item>
/// </list>
/// Selecting a session item resumes it; the chat page is rebuilt when
/// <see cref="ISessionNavigator.SessionChanged"/> fires. The navigation and
/// view-switching logic lives in <see cref="DeskShell"/>; this class is the
/// thin window host that wires it to a live MewUI application.
/// </summary>
public sealed class PhiDeskApp : IDisposable
{
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;
    private Window? _window;
    private DeskShell? _shell;

    public PhiDeskApp(ISessionNavigator navigator, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        _navigator = navigator;
        _providers = providers;
    }

    /// <summary>
    /// Mounts the chat window and starts MewUI's render loop. The window
    /// content is a <see cref="NavigationView"/> whose right region is
    /// rebuilt on navigation. Mirrors the upstream gallery demo's builder
    /// flow: the accent is applied, then the window is created via
    /// <c>BuildMainWindow</c>, then the render loop runs.
    /// </summary>
    public void Run()
    {
        Application.Create()
            .UseAccent(Accent.Purple)
            .BuildMainWindow(BuildWindow)
            .Run();
    }

    private Window BuildWindow()
    {
        _window = new Window()
            .Resizable(1024, 720)
            .StartCenterScreen()
            .Title("Phi")
            .Padding(0)
            .OnClosed(Dispose);

        _shell = new DeskShell(
            _navigator,
            _providers,
            owner: _window,
            dispatchToUi: action =>
            {
                // Before Application.Run starts (window/shell construction) we
                // are already on the UI thread and Application.Current throws;
                // run inline. Once running, marshal through the dispatcher.
                if (!Application.IsRunning)
                {
                    action();
                    return;
                }
                var dispatcher = Application.Current.Dispatcher;
                if (dispatcher is null) return;
                dispatcher.Invoke(action);
            },
            postToUi: action =>
            {
                if (!Application.IsRunning)
                {
                    action();
                    return;
                }
                var dispatcher = Application.Current.Dispatcher;
                if (dispatcher is null) return;
                dispatcher.BeginInvoke(action);
            });

        _window.Content = _shell.BuildRoot();
        return _window;
    }

    /// <summary>
    /// Releases the app's session subscriptions and the live chat page when
    /// the window closes. The MewUI element tree is disposed by the
    /// framework itself on close.
    /// </summary>
    public void Dispose()
    {
        _shell?.Dispose();
        _shell = null;
    }
}
