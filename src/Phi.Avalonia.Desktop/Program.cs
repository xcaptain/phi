using Avalonia;

namespace Phi.Avalonia.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things
    // aren't initialized yet and stuff might break.
    //
    // Composition.InitializeAsync is awaited first so the static
    // Composition.ActiveSession / Composition.Providers singletons are
    // ready when App.OnFrameworkInitializationCompleted constructs the
    // ShellViewModel. After the async composition completes we fall
    // through to the sync StartWithClassicDesktopLifetime which blocks
    // until the application exits — same pattern the older
    // Phi.Avalonia.Desktop uses.
    [STAThread]
    public static async Task Main(string[] args)
    {
        await Composition.InitializeAsync();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
