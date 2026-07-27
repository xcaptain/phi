using PhiAgent;
using PhiCoding;
using PhiCoding.Tui;
using PhiProvider;

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

var bashTool = new BashTool();
var readTool = new ReadTool();
var writeTool = new WriteTool();
var editTool = new EditTool();

var tools = new List<Tool>
{
    bashTool.Definition,
    readTool.Definition,
    writeTool.Definition,
    editTool.Definition,
};

var harness = new Harness(
    provider,
    tools: tools,
    executeTool: (name, id, args, ct) => name switch
    {
        "bash" => bashTool.ExecuteAsync(id, args, ct),
        "read" => readTool.ExecuteAsync(id, args, ct),
        "write" => writeTool.ExecuteAsync(id, args, ct),
        "edit" => editTool.ExecuteAsync(id, args, ct),
        _ => throw new NotSupportedException($"Unknown tool: {name}"),
    },
    model: "deepseek-v4-flash",
    system: """
        You have four tools: bash, read, write, edit.
        Use read to inspect files before editing them.
        Use edit for surgical changes (old_string must be unique).
        Use write for new files or full rewrites.
        Use bash for shell inspection and commands.
        Be concise.
        """);

new PhiTuiApp(harness).Run();
return 0;