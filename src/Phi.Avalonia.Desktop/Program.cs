using Avalonia;
using Phi.Providers;

namespace Phi.Avalonia.Desktop;

/// <summary>
/// Desktop entry point. Composes the provider manager, the cross-session
/// <see cref="Phi.SessionEnvironment"/>, the initial
/// <see cref="Phi.Session"/>, and the UI's
/// <see cref="Phi.Avalonia.ActiveSession"/> holder, then hands them to
/// the Avalonia app and starts the classic desktop lifetime.
/// <para>
/// Session <em>switching</em> after this point is owned by
/// <see cref="Phi.ISession.NewSessionAsync"/> /
/// <see cref="Phi.ISession.ResumeAsync"/>: the new session is returned,
/// the old one disposes itself, and the <see cref="ActiveSession"/>
/// holder fires its <c>Changed</c> event so the shell rebuilds. No
/// separate navigator.
/// </para>
/// </summary>
internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
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

        // Composition root: wire the provider manager into a
        // SessionEnvironment (the cross-session context shared by every
        // session the app will ever create) and use it to build the
        // initial Session. The desktop isn't bound to a process working
        // directory, so the default cwd is the most recently used
        // workspace derived from session records (falling back to the
        // launch directory when no sessions exist yet).
        var providerManager = new ProviderManager();
        var defaultProvider = providerManager.ResolveDefaultProvider();
        var env = SessionEnvironment.Default(providerManager);

        var recentWorkspaces = WorkspaceSessionStore.ListWorkspaces();
        var defaultCwd = recentWorkspaces.Count > 0
            ? recentWorkspaces[0]
            : Environment.CurrentDirectory;

        Phi.Session session;
        try
        {
            session = await Phi.Session.LoadAsync(
                defaultCwd, env,
                providerName: defaultProvider.Name,
                model: providerManager.ResolveDefaultModel(defaultProvider),
                resumeId: resumeSessionId);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        using (session)
        {
            var active = new ActiveSession(session);
            BuildAvaloniaApp(active, providerManager).StartWithClassicDesktopLifetime(args);
        }
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp(ActiveSession active, ProviderManager providers) =>
        AppBuilder.Configure(() => new PhiAvaloniaApp(active, providers))
            .UsePlatformDetect()
            .LogToTrace();
}
