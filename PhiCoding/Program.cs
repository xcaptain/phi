using PhiAgent;
using PhiCoding;
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

// TODO: 应该使用一个更通用的办法来加载模型，也许是一个 yaml 文件
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

var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine(
        "DEEPSEEK_API_KEY is empty. Set it in your shell or put it in .env at the repo root.");
    return 1;
}

const string model = "deepseek-v4-flash";

// Composition root: build the concrete provider here so CodingSession
// only ever sees the IPhiProvider abstraction.
IPhiProvider provider = new AnthropicProvider(
    new AnthropicConfig
    {
        ApiKey = apiKey,
        BaseUrl = "https://api.deepseek.com/anthropic",
        Provider = "deepseek",
    },
    new HttpClient());

var config = new SessionConfig
{
    Cwd = Environment.CurrentDirectory,
    Provider = provider,
    Model = model,
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

using (var app = new PhiTuiApp(session))
{
    app.Run();
}
return 0;
