using PhiAgent;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui;

/// <summary>
/// Default card shape: a title <see cref="Markup"/> on top, a body <see cref="Visual"/>
/// below that is swap-friendly via <see cref="BodyState"/>. Used by write/edit/bash
/// (and the generic fallback). <see cref="ReadToolCard"/> overrides this with a
/// single-line <see cref="Markup"/> because reads render no body at all.
/// </summary>
public abstract class ToolCardBase : IToolCard
{
    internal const int PreviewMaxLines = 8;
    internal const int PreviewMaxChars = 2000;

    protected ToolCardBase()
    {
        Visual = new Group(TitleMarkup, new ComputedVisual(() => BodyState.Value))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Start)
            .Padding(1);
    }

    public Visual Visual { get; }

    /// <summary>
    /// Current body visual. Defaults to a dim placeholder, then replaced by
    /// the tool-specific body (diff grid, truncated output, etc.) on
    /// <see cref="Complete"/>. Exposed for tests and for callers that need
    /// to swap in additional visuals.
    /// </summary>
    public State<Visual> BodyState { get; } = new(new Markup("[dim]…[/]") { Wrap = false });

    /// <summary>Current title markup text. Tests use this for assertions.</summary>
    public string Title => TitleMarkup.Text ?? "";

    protected Markup TitleMarkup { get; } = new("");

    public void ShowPending(ToolCall toolCall)
    {
        Call = toolCall;
        OnShowPending(toolCall);
        BodyState.Value = new Markup("[dim]…[/]") { Wrap = false };
    }

    public void Complete(ToolResult result)
    {
        var toolCall = Call ?? throw new InvalidOperationException(
            $"{GetType().Name}.Complete called before ShowPending.");
        OnComplete(toolCall, result);
    }

    protected ToolCall? Call { get; private set; }

    protected abstract void OnShowPending(ToolCall toolCall);
    protected abstract void OnComplete(ToolCall toolCall, ToolResult result);

    internal static string Escape(string text) => text.Replace("[", "\\[").Replace("]", "\\]");

    /// <summary>
    /// Renders a tool result as a truncated, escaped <see cref="Markup"/> body.
    /// <paramref name="style"/> is the XenoAtom markup style name (e.g. <c>"dim"</c>,
    /// <c>"red"</c>) applied per non-empty line; a trailing dim note is appended
    /// when lines or characters were dropped.
    /// </summary>
    internal static Markup TruncatedOutputBody(ToolResult result, string style)
    {
        var lines = TruncateLines(result.Text, PreviewMaxLines, PreviewMaxChars,
            out var hidden, out var charTruncated);
        var body = string.Join('\n', lines.Select(l => $"[{style}]{Escape(l)}[/]"));
        if (hidden > 0 || charTruncated)
        {
            var note = hidden > 0 ? $"{hidden} more lines" : "output";
            body += $"\n[dim]… ({note} hidden)[/]";
        }
        return new Markup(body) { Wrap = true };
    }

    internal static IReadOnlyList<string> TruncateLines(
        string text, int maxLines, int maxChars, out int hiddenLines, out bool charTruncated)
    {
        var truncated = text.Length > maxChars ? text[..maxChars] : text;
        charTruncated = truncated.Length < text.Length;
        var all = truncated.Replace("\r\n", "\n").Split('\n');
        hiddenLines = Math.Max(0, all.Length - maxLines);
        return all.Length > maxLines ? all[..maxLines] : all;
    }
}
