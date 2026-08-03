using PhiAgent;
using PhiCoding;
using PhiCoding.Providers;
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
    SystemPrompt = """
        You are an expert coding assistant operating inside Phi a coding agent harness.
        You have four tools: bash, read, write, edit.

        Use read to inspect files before editing them.
        For large files, use offset/limit on read to read a slice at a time
        and increment offset to continue. Do not use cat, sed, or head to read files.
        Use edit for surgical changes (old_string must be unique).
        Use write for new files or full rewrites.
        Use bash for shell inspection and commands.
        Be concise.
        """,
    MaxTurns = 50,
};

CodingSession session;
try
{
    session = resumeSessionId is null
        ? CodingSession.Create(config)
        : CodingSession.Resume(config, resumeSessionId);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

using (var app = new PhiTuiApp(session, providerManager))
{
    app.Run();
}
return 0;
