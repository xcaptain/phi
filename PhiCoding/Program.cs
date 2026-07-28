using PhiAgent;
using PhiCoding;
using PhiCoding.Tui;
using PhiProvider;

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

var provider = new OpenAICompatibleProvider(
    new OpenAICompatibleConfig
    {
        ApiKey = apiKey,
        BaseUrl = "https://api.deepseek.com",
        Provider = "deepseek",
    },
    new HttpClient());

var tools = BuiltInTools.CreateDefault();

const string model = "deepseek-v4-flash";

var harness = new Harness(
    provider,
    tools,
    model: model,
    system: """
        You are an expert coding assistant operating inside Phi a coding agent harness.
        You have four tools: bash, read, write, edit.
        Use read to inspect files before editing them.
        Use edit for surgical changes (old_string must be unique).
        Use write for new files or full rewrites.
        Use bash for shell inspection and commands.
        Be concise.
        """,
    maxTurns: 50);

new PhiTuiApp(harness, model).Run();
return 0;