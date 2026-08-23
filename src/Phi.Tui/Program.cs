using Phi;
using Phi.Providers;
using Phi.Tui;

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

// Composition root: wire the provider manager into a SessionEnvironment
// (the cross-session context shared by every Session the TUI will ever
// create) and use it to build the initial Session. Subsequent /new and
// /sessions are handled by ISession.NewSessionAsync / ResumeAsync — no
// separate navigator. Provider construction lives inside Session.LoadAsync,
// which resolves via SessionEnvironment.ProviderResolver and falls back to
// a no-op provider when no API key exists (so the TUI can open and prompt
// for /connect).
var providerManager = new ProviderManager();
var defaultProvider = providerManager.ResolveDefaultProvider();
var env = SessionEnvironment.Default(providerManager);

Session session;
try
{
    session = await Session.LoadAsync(
        Environment.CurrentDirectory, env,
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
    var app = new PhiTuiApp(session, providerManager);
    app.Run();
}
return 0;
