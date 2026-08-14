using Avalonia;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Avalonia.Desktop;

/// <summary>
/// Desktop entry point. Composes the provider manager, session factory,
/// and navigator, then hands them to the Avalonia app. Mirrors the MewUI
/// desk's Program.cs; the only difference is the UI stack.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Capture any unhandled UI / background exception to a log file so
        // workspace-picker and submit failures can be diagnosed without a
        // console.
        var errorLogPath = Path.Combine(SessionPaths.PhiHome, "avalonia-errors.log");
        void LogError(Exception ex)
        {
            try
            {
                File.AppendAllText(
                    errorLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {ex}{Environment.NewLine}");
            }
            catch
            {
                // ignore log write failures
            }
        }
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogError(e.Exception);
            e.SetObserved();
        };

        // ──────── CLI args ────────
        // phi-avalonia                → fresh session (persisted lazily)
        // phi-avalonia --session <id> → resume an indexed session
        string? resumeSessionId = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--session" && i + 1 < args.Length)
            {
                resumeSessionId = args[++i];
            }
            else
            {
                Console.Error.WriteLine($"Unknown argument: {args[i]}");
                Console.Error.WriteLine("Usage: phi-avalonia [--session <id>]");
                return 1;
            }
        }

        EnvLoader.LoadDotEnv();

        // Composition root: wire the provider manager (catalog + credentials
        // + settings) into a session factory and a navigator. Provider
        // construction is entirely the factory's job — it resolves the
        // provider from the config name via the manager and falls back to a
        // no-op provider when no API key exists.
        var providerManager = new ProviderManager();
        var factory = new CodingSessionFactory(providerManager);
        var defaultProvider = providerManager.ResolveDefaultProvider();

        // The desktop isn't bound to a process working directory, so the
        // default cwd is the most recently used workspace derived from
        // session records (falling back to the launch directory when no
        // sessions exist yet).
        var recentWorkspaces = WorkspaceSessionStore.ListWorkspaces();
        var env = new SessionConfig
        {
            Cwd = recentWorkspaces.Count > 0 ? recentWorkspaces[0] : Environment.CurrentDirectory,
            ProviderName = defaultProvider.Name,
            Model = providerManager.ResolveDefaultModel(defaultProvider),
        };

        SessionNavigator navigator;
        try
        {
            navigator = new SessionNavigator(factory, env, resumeSessionId);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        using (navigator)
        {
            BuildAvaloniaApp(navigator, providerManager).StartWithClassicDesktopLifetime(args);
        }
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp(ISessionNavigator navigator, ProviderManager providers) =>
        AppBuilder.Configure(() => new PhiAvaloniaApp(navigator, providers))
            .UsePlatformDetect()
            .LogToTrace();
}
