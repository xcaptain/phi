using PhiAgent;
using PhiCoding;
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

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project PhiCoding -- <prompt>");
    return 1;
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
var harness = new Harness(
    provider,
    tools: [bashTool.Definition],
    executeTool: (name, id, args, ct) => name == "bash"
        ? bashTool.ExecuteAsync(id, args, ct)
        : throw new NotSupportedException($"Unknown tool: {name}"),
    model: "deepseek-v4-flash",
    system: "You have a bash tool. Use it to inspect the system when needed. Be concise.");

var prompt = args[0];
Console.WriteLine($"[prompt] {prompt}\n");

try
{
    await foreach (var ev in harness.RunAsync(prompt))
    {
        switch (ev)
        {
            case TurnStartEvent ts:
                Console.WriteLine($"\n[turn {ts.Turn}]");
                break;
            case AssistantTextDeltaEvent t:
                Console.Write(t.Delta);
                break;
            case AssistantToolCallEvent tc:
                Console.WriteLine($"\n[tool] {tc.ToolCall.Name}({tc.ToolCall.Id})");
                break;
            case ToolExecutionEndEvent te:
                Console.WriteLine($"  → {te.Result.Text.Replace("\n", "\n  ")}");
                break;
            case TurnEndEvent te:
                Console.WriteLine($"\n[stop] {te.FinalMessage.StopReason}");
                break;
        }
    }
}
catch (InvalidOperationException ex) when (ex.Message.Contains("Provider produced no ProviderResponseEndEvent"))
{
    Console.Error.WriteLine($"[error] {ex.Message}");
    Console.Error.WriteLine("[hint] Check DEEPSEEK_API_KEY, network, and the BaseUrl.");
    return 1;
}
return 0;