using Aprillz.MewUI;
using PhiCoding;
using PhiCoding.Desk;
using PhiCoding.Providers;
using PhiCoding.Sessions;

// Capture any unhandled UI / background exception to a log file so
// workspace-picker and submit failures can be diagnosed without a console.
var errorLogPath = Path.Combine(SessionPaths.PhiHome, "desk-errors.log");
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
Application.DispatcherUnhandledException += e =>
{
    LogError(e.Exception);
    e.Handled = true;
};
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    LogError(e.Exception);
    e.SetObserved();
};

// ──────── CLI args ────────
// phi-desk                → /sessions/new (fresh session, persisted lazily)
// phi-desk --session <id> → /sessions/:id (resume an indexed session)
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
        Console.Error.WriteLine("Usage: phi-desk [--session <id>]");
        return 1;
    }
}

EnvLoader.LoadDotEnv();
BackendRegistrar.Register();

// Composition root: wire the provider manager (catalog + credentials +
// settings) into a session factory and a navigator, pick the startup route
// from the CLI, and hand the navigator to the desktop shell. Provider
// construction is entirely the factory's job — it resolves the provider
// from the config name via the manager and falls back to a no-op provider
// when no API key exists (so the desktop can open and prompt for /connect).
var providerManager = new ProviderManager();
var factory = new CodingSessionFactory(providerManager);
var defaultProvider = providerManager.ResolveDefaultProvider();

// Environment for any session: cwd, prompt, tools, compaction knobs. The
// desktop isn't bound to a process working directory, so the default cwd is
// the most recently used workspace derived from session records (falling
// back to the launch directory when no sessions exist yet). On a fresh
// session the default provider/model apply; on resume the session record's
// provider/model win (the config only supplies the environment).
var recentWorkspaces = WorkspaceSessionStore.ListWorkspaces();
var env = new SessionConfig
{
    Cwd = recentWorkspaces.Count > 0 ? recentWorkspaces[0] : Environment.CurrentDirectory,
    ProviderName = defaultProvider.Name,
    Model = providerManager.ResolveDefaultModel(defaultProvider),
};

PhiCoding.Sessions.SessionNavigator navigator;
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
    var app = new PhiDeskApp(navigator, providerManager);
    app.Run();
}
return 0;
