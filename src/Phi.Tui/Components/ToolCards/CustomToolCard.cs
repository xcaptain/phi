using Phi.Agent;
using Phi.Extensions.Rendering;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Phi.Tui.Components.ToolCards;

/// <summary>
/// Card produced for a tool whose card was overridden by an extension via
/// <c>IPhiApi.RegisterToolCard</c>. The extension's
/// <see cref="ToolCardRenderer"/> produces the body content on completion;
/// the returned <see cref="object"/> is cast to a XenoAtom <see cref="Visual"/>
/// and used as the card body. If the renderer returns something the TUI
/// can't render (or returns null), the card falls back to the generic
/// truncated-output body.
/// <para>
/// Pending state mirrors the generic card: a <c>› toolName</c> title.
/// The renderer is only invoked once the tool result arrives (it receives
/// the tool's arguments + the completed <see cref="ToolResult"/>).
/// </para>
/// </summary>
public sealed class CustomToolCard : ToolCardBase
{
    private readonly ToolCardRenderer _renderer;

    public CustomToolCard(ToolCardRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    protected override void OnShowPending(ToolCall toolCall)
    {
        TitleMarkup.Text = $"[primary]→ {ToolCardBase.Escape(toolCall.Name)}[/]";
        BodyState.Value = new Markup("[dim]…[/]") { Wrap = false };
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var status = result.IsError ? "[red]✗[/]" : "[green]✓[/]";
        TitleMarkup.Text = $"{status} [primary]→ {ToolCardBase.Escape(toolCall.Name)}[/]";

        // The extension's renderer produces the body; only use it when it
        // yields something the TUI can actually render.
        object? fragment;
        try
        {
            fragment = _renderer(toolCall.Arguments, result);
        }
        catch
        {
            fragment = null; // a throwing renderer must not break the transcript
        }

        if (fragment is Visual visual)
        {
            BodyState.Value = visual;
            return;
        }

        if (fragment is string text)
        {
            BodyState.Value = new Markup(ToolCardBase.Escape(text)) { Wrap = true };
            return;
        }

        BodyState.Value = TruncatedOutputBody(result, result.IsError ? "red" : "dim");
    }
}
