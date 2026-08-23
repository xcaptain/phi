using System.Text.Json.Nodes;
using Phi.Agent;

namespace Phi.Extensions.HelloTool;

/// <summary>
/// Sprint 1 reference extension: registers a "hello" tool and a "/hello"
/// slash command. Demonstrates the minimal contract extension authors must
/// satisfy and validates that the <c>Phi.Extensions.Host</c> pipeline
/// (load → setup → register tool → invoke) works end-to-end.
/// <para>
/// Sprint 1 limitation: the <c>/hello</c> command is registered but not
/// yet dispatched (UI's <c>HandleInput</c> still consults a hard-coded
/// switch). The slash registration records a diagnostic in
/// <c>ExtensionRuntime.SetupResults</c>. Real dispatch lands in Sprint 2.
/// </para>
/// </summary>
[PhiExtension(
    Name = "hello-tool",
    Version = "1.0.0",
    Description = "Greet someone by name.",
    Capabilities = ExtensionCapability.None)]
public sealed class HelloToolExt : IPhiExtension
{
    public void Setup(IPhiApi api)
    {
        api.RegisterTool(
            new HelloToolImpl(),
            new ToolContribution
            {
                Tool = new HelloToolImpl(),
                PromptSnippet = "hello: Greet someone by name.",
                PromptGuidelines = ["Use hello when asked to greet someone."],
            });

        api.AddPromptGuideline("When the user says hi, greet them back with hello(\"world\").");

        api.RegisterCommand(
            "/hello",
            (args, _) =>
            {
                var who = string.IsNullOrWhiteSpace(args) ? "world" : args;
                api.SubmitUserMessage($"Say hello to {who}");
                return null;
            },
            description: "Say hello to someone.",
            usage: "/hello [name]",
            aliases: ["/hi"]);
    }
}

internal sealed class HelloToolImpl : Tool
{
    public override string Name => "hello";

    public override string Description =>
        "Greet someone by name. Returns a friendly one-line greeting.";

    public override JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["who"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Name to greet. Defaults to \"world\" when omitted.",
            },
        },
        ["required"] = new JsonArray(),   // no required fields
    };

    public override async Task<ToolResult> ExecuteAsync(
        string toolName,
        string toolCallId,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var who = arguments["who"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(who)) who = "world";
        // Simulate tiny async work so cancellation is testable (Sprint 2+).
        await Task.Yield();
        return new ToolResult(
            [new TextBlock($"Hello, {who}!")],
            IsError: false);
    }
}
