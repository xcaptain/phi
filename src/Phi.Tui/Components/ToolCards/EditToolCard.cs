using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Tools.Details;

namespace Phi.Tui.Components.ToolCards;

/// <summary>
/// Card for <c>edit</c>: title carries the path + block count summary, body is a
/// <see cref="SideBySideDiff"/> grid on success and a truncated red error body
/// on failure. Diff construction is delegated to <see cref="SideBySideDiff"/>
/// because it owns the DiffPlex plumbing.
/// </summary>
public sealed class EditToolCard : ToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        TitleMarkup.Text = $"[primary]→ edit {GetString(toolCall.Arguments, "path")}[/]";
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var edit = ToolDetails.Read<EditDetails>(result.Details);
        var path = edit?.Path ?? GetString(toolCall.Arguments, "path");
        var summary = edit is not null ? $"edit {path} · {edit.Edits.Count} block(s)" : "edit";
        var status = result.IsError ? "[red]✗[/]" : "[green]✓[/]";
        TitleMarkup.Text = $"{status} [primary]→ edit {path}[/] [dim]· {ToolCardBase.Escape(summary)}[/]";

        if (!result.IsError && edit is not null)
            BodyState.Value = SideBySideDiff.Build(edit);
        else
            BodyState.Value = TruncatedOutputBody(result, "red");
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
