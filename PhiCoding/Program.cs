using PhiCoding;
using PhiCoding.Providers;
using PhiCoding.Routing;
using PhiCoding.Sessions;
using PhiCoding.Tui;

// ──────── CLI args ────────
// phi                  → /sessions/new (fresh session, persisted lazily)
// phi --session <id>   → /sessions/:id (resume an indexed session)
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
        Console.Error.WriteLine("Usage: phi [--session <id>]");
        return 1;
    }
}

// Load .env from cwd (dotnet does not auto-load .env files)
if (File.Exists(".env"))
{
    foreach (var line in File.ReadAllLines(".env"))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
        var eq = trimmed.IndexOf('=');
        if (eq <= 0) continue;
        var key = trimmed[..eq].Trim();
        var value = trimmed[(eq + 1)..].Trim().Trim('"', '\'');
        Environment.SetEnvironmentVariable(key, value);
    }
}

// Composition root: wire the provider manager (catalog + credentials +
// settings) into a session factory and a navigator, pick the startup route
// from the CLI, and hand the navigator to the TUI. Provider construction is
// entirely the factory's job — it resolves the provider from the config name
// via the manager and falls back to a no-op provider when no API key exists
// (so the TUI can open and prompt for /connect).
var providerManager = new ProviderManager();
var factory = new CodingSessionFactory(providerManager);
var defaultProvider = providerManager.ResolveDefaultProvider();

// Environment for any session: cwd, prompt, tools, compaction knobs. On a
// fresh session the default provider/model apply; on resume the session
// record's provider/model win (the config only supplies the environment).
var env = new SessionConfig
{
    Cwd = Environment.CurrentDirectory,
    ProviderName = defaultProvider.Name,
    Model = providerManager.ResolveDefaultModel(defaultProvider),
};

AppRoute route = resumeSessionId is null
    ? new ChatRoute(new NewSessionRequest())
    : new ChatRoute(new ExistingSessionRequest(resumeSessionId));

SessionNavigator navigator;
try
{
    navigator = new SessionNavigator(factory, env, route);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

using (navigator)
{
    var app = new PhiTuiApp(navigator, providerManager);
    app.Run();
}
return 0;
