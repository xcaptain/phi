using PhiAgent;

namespace PhiCoding.Tui;

/// <summary>
/// Fallback card for tool names the registry does not know (e.g. MCP tools in
/// the future). Renders a generic title and a truncated output body. Keeps the
/// transcript rendering pipeline uniform so the registry can always return a
/// non-null <see cref="IToolCard"/>.
/// </summary>
public sealed class GenericToolCard : ToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        TitleMarkup.Text = $"[primary]→ {toolCall.Name}[/]";
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var status = result.IsError ? "[red]✗[/]" : "[green]✓[/]";
        TitleMarkup.Text = $"{status} [primary]→ {toolCall.Name}[/] [dim]· {ToolCardBase.Escape(toolCall.Name)}[/]";
        BodyState.Value = TruncatedOutputBody(result, result.IsError ? "red" : "dim");
    }
}
