using System.ComponentModel;
using Phi.Agent;

namespace Phi.Extensions.CustomCardDemo;

/// <summary>
/// Optional short text rendered by the demo tool card. Nullable / not
/// required so the model can omit it; the tool falls back to a friendly
/// default string.
/// </summary>
public sealed record DemoArgs
{
    [Description("Optional short text to render in the demo card")]
    public string? Text { get; init; }
}

/// <summary>
/// Tiny tool whose result is intentionally simple — the point of this
/// demo is the custom card / transcript-line rendering, not the tool
/// logic itself.
/// </summary>
public sealed partial class DemoTool : TypedTool<DemoArgs>
{
    public override string Name => "demo";

    public override string Description =>
        "Return a short payload that the custom card renderer formats.";

    public override Task<ToolResult> ExecuteTypedAsync(DemoArgs args, CancellationToken cancellationToken)
    {
        var text = string.IsNullOrWhiteSpace(args.Text) ? "hello from demo" : args.Text;
        return Task.FromResult(new ToolResult([new TextBlock(text)]));
    }
}

/// <summary>
/// Reference extension for Sprint 4: registers one tool, one custom tool
/// card, and one transcript-line renderer. The card/line renderers both
/// return plain strings so the same extension works in both TUI and
/// Avalonia — each host wraps the string in its own default text body.
/// </summary>
[PhiExtension(
    Name = "custom-card-demo",
    Version = "1.0.0",
    Description = "Demo extension for custom tool cards and transcript lines.",
    Capabilities = ExtensionCapability.TranscriptWrite)]
public sealed class CustomCardDemoExt : IPhiExtension
{
    public void Setup(IPhiApi api)
    {
        var tool = new DemoTool();
        api.RegisterTool(
            tool,
            new ToolContribution
            {
                Tool = tool,
                PromptSnippet = "demo: produce a short payload that the custom card renders.",
                PromptGuidelines = ["Use demo to exercise custom card rendering when asked to show a host UI example."],
            });

        api.RegisterToolCard(
            "demo",
            new ToolDescriptor(ToolKind.Generic, "demo", "🎨"),
            renderer: (args, result) =>
            {
                var text = result.Text;
                var input = args["text"]?.ToString();
                return string.IsNullOrWhiteSpace(input)
                    ? $"demo card → {text}"
                    : $"demo card ({input}) → {text}";
            });

        api.RegisterTranscriptLineRenderer(
            "custom-card-demo:notice",
            (line, Expanded) =>
            {
                var level = line.Details?.TryGetValue("level", out var l) == true
                    ? l?.ToString() ?? "info"
                    : "info";
                return $"[{level}] {line.Content}";
            });

        api.AddPromptGuideline(
            "When asked to demonstrate a custom host card, use the demo tool; the host renders it with a custom card.");
    }
}
