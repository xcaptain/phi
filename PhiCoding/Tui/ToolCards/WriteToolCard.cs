using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding.Tools.Details;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui.ToolCards;

/// <summary>
/// Card for <c>write</c>: title carries the path + bytes/mode summary, body is
/// empty on success (the user already saw the call invocation) and a truncated
/// red error body on failure.
/// </summary>
public sealed class WriteToolCard : ToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        TitleMarkup.Text = $"[primary]→ write {GetString(toolCall.Arguments, "path")}[/]";
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var status = result.IsError ? "[red]✗[/]" : "[green]✓[/]";
        var summary = ToolDetails.Read<WriteDetails>(result.Details) is { } d
            ? $"write — {d.BytesWritten} bytes ({d.Mode})"
            : "write";
        TitleMarkup.Text = $"{status} [primary]→ write {GetString(toolCall.Arguments, "path")}[/] [dim]· {ToolCardBase.Escape(summary)}[/]";

        BodyState.Value = result.IsError
            ? TruncatedOutputBody(result, "red")
            : new Markup("");
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
