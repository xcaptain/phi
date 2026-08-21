using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Tools.Details;

namespace Phi.Tui.Components.ToolCards;

/// <summary>
/// Card for <c>bash</c>: title is <c>$ command</c> + exit/duration summary, body
/// is the truncated output (dim on success, red on error).
/// </summary>
public sealed class BashToolCard : ToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        TitleMarkup.Text = $"[primary]$ {GetString(toolCall.Arguments, "command")}[/]";
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var bash = ToolDetails.Read<BashDetails>(result.Details);
        var command = bash?.Command ?? GetString(toolCall.Arguments, "command");
        var summary = bash is not null
            ? $"bash — exit={bash.ExitCode} in {bash.DurationMs}ms"
            : "bash";
        var status = result.IsError ? "[red]✗[/]" : "[green]✓[/]";
        TitleMarkup.Text = $"{status} [primary]$ {command}[/] [dim]· {ToolCardBase.Escape(summary)}[/]";

        BodyState.Value = TruncatedOutputBody(result, result.IsError ? "red" : "dim");
    }

    private static string GetString(JsonNode? args, string key)
    {
        if (args is JsonObject o
            && o.TryGetPropertyValue(key, out var v)
            && v is JsonValue jv
            && jv.TryGetValue<string>(out var s))
            return s;
        return "";
    }
}
