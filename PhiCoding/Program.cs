using PhiAgent;
using PhiCoding;
using PhiCoding.Prompts;
using PhiCoding.Providers;
using PhiCoding.Sessions;
using PhiCoding.Tui;
using PhiProvider;

// ──────── CLI args ────────
// phi                  → fresh session (persisted lazily on first message)
// phi --session <id>   → resume an indexed session
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

// Composition root: resolve the default provider from settings, build the
// runtime provider from its API key (env var → credential store), and hand it
// to the session. The TUI also receives the ProviderManager so /connect and
// /models can switch provider/model at runtime.
var providerManager = new ProviderManager();
var defaultProvider = providerManager.ResolveDefaultProvider();
var defaultModel = providerManager.ResolveDefaultModel(defaultProvider);

IPhiProvider provider;
if (providerManager.ResolveApiKey(defaultProvider) is { } apiKey)
{
    provider = providerManager.CreateProvider(defaultProvider, apiKey);
}
else
{
    // No key for the default provider yet: start with a placeholder so the
    // TUI opens and the user can run /connect instead of failing hard.
    provider = new NullProvider();
}

var config = new SessionConfig
{
    Cwd = Environment.CurrentDirectory,
    Provider = provider,
    ProviderName = defaultProvider.Name,
    Model = defaultModel,
    SystemPrompt = new SystemPromptOptions(),
    MaxTurns = 50,
};

var factory = new CodingSessionFactory(providerManager);
CodingSession session;
try
{
    if (resumeSessionId is null)
    {
        // Fresh: the startup provider (built above from the default name)
        // is the one the session owns and disposes on exit.
        session = factory.Create(config);
    }
    else
    {
        // Resuming: the session record's provider/model win by default so a
        // later switch is not silently undone by the current default. The
        // config only overrides when the caller explicitly sets a value
        // (no --model/--provider CLI flags exist yet, so record wins today).
        // The factory rebuilds the live provider from record.ProviderName
        // via the resolver, so API key, base URL, and HTTP transport all
        // come back to the recorded provider — not the startup default.
        var resumeConfig = config with
        {
            Model = "",
            ProviderName = "",
            Provider = null,
        };
        session = factory.Resume(resumeConfig, resumeSessionId);
    }
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

using var app = new PhiTuiApp(session, providerManager);
app.Run();
return 0;
