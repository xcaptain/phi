using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PhiAgent;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace PhiCoding.Avalonia.Components.ToolCards;

/// <summary>
/// Per-tool card on the desktop side. The card visualizes one
/// <see cref="PhiCoding.Chat.ToolCallLine"/>: a title row (status +
/// invocation) plus an optional body. Pending state shows a placeholder
/// body; on <see cref="Complete"/> the body swaps to a tool-specific
/// summary (output preview for bash, etc.).
/// </summary>
public interface IAvaloniaToolCard
{
    Control Visual { get; }
    void ShowPending(ToolCall toolCall);
    void Complete(ToolResult result);
}

/// <summary>
/// Resolves the <see cref="IAvaloniaToolCard"/> implementation for a given
/// tool name. Adding a new tool means writing one
/// <see cref="AvaloniaToolCardBase"/> subclass and adding a switch arm
/// here — same shape as the TUI's <c>ToolCardRegistry</c>.
/// </summary>
public static class AvaloniaToolCardRegistry
{
    public static IAvaloniaToolCard For(string name) => name switch
    {
        "read" => new ReadToolCardView(),
        "write" => new WriteToolCardView(),
        "edit" => new EditToolCardView(),
        "bash" => new BashToolCardView(),
        _ => new GenericToolCardView(),
    };
}

/// <summary>Shared helpers: JSON argument lookup + status prefixes.</summary>
internal static class AvaloniaToolCardHelpers
{
    public static string GetString(JsonNode? args, string key)
    {
        if (args is JsonObject o
            && o.TryGetPropertyValue(key, out var v)
            && v is JsonValue jv
            && jv.TryGetValue<string>(out var s))
            return s;
        return string.Empty;
    }

    public static int? TryGetInt(JsonNode? args, string key)
    {
        if (args is not JsonObject o) return null;
        if (!o.TryGetPropertyValue(key, out var v)) return null;
        if (v is JsonValue jv && jv.TryGetValue<long>(out var n)) return (int)n;
        if (v is JsonValue jv2 && jv2.TryGetValue<int>(out var i)) return i;
        return null;
    }

    public static string FormatBytes(int n) => n switch
    {
        < 1024 => $"{n}B",
        < 1024 * 1024 => $"{n / 1024.0:F1}KB",
        _ => $"{n / 1024.0 / 1024.0:F1}MB",
    };

    public static string StatusPrefix(bool isError) =>
        isError ? "✗" : "✓";

    public static TextBlock BodyText(string text, bool mono = false, IBrush? foreground = null) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = mono ? new FontFamily("Consolas,Menlo,Monospace") : FontFamily.Default,
            Foreground = foreground ?? AvaloniaTheme.TextSecondary,
        };
}

/// <summary>
/// Default card shape: a header row (status + invocation) plus a body
/// that <see cref="ShowPending"/> initializes as a placeholder and
/// <see cref="Complete"/> replaces with the tool-specific body.
/// </summary>
public abstract class AvaloniaToolCardBase : IAvaloniaToolCard
{
    private ToolCall? _call;
    private readonly TextBlock _titleBlock;
    private readonly ContentControl _bodyHolder = new();

    protected AvaloniaToolCardBase()
    {
        _titleBlock = new TextBlock { FontWeight = FontWeight.SemiBold };
        Visual = new Border
        {
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(6),
            Background = AvaloniaTheme.ContainerBackground,
            BorderBrush = AvaloniaTheme.ControlBorder,
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 4,
                Children = { _titleBlock, _bodyHolder },
            },
        };
    }

    public Control Visual { get; }

    public void ShowPending(ToolCall toolCall)
    {
        _call = toolCall;
        OnShowPending(toolCall);
        _bodyHolder.Content = new TextBlock { Text = "…", Foreground = AvaloniaTheme.TextSecondary };
    }

    public void Complete(ToolResult result)
    {
        var toolCall = _call ?? throw new InvalidOperationException(
            $"{GetType().Name}.Complete called before ShowPending.");
        OnComplete(toolCall, result);
    }

    protected void SetTitle(string text) => _titleBlock.Text = text;
    protected void SetBody(Control? body) => _bodyHolder.Content = body;

    protected abstract void OnShowPending(ToolCall toolCall);
    protected abstract void OnComplete(ToolCall toolCall, ToolResult result);
}

/// <summary>
/// One-line card for <c>read</c>: no body, no placeholder, just a title
/// that flips from invocation to status+invocation+summary when the
/// result lands.
/// </summary>
public sealed class ReadToolCardView : IAvaloniaToolCard
{
    private readonly TextBlock _titleBlock = new();
    private ToolCall? _call;

    public Control Visual => _titleBlock;

    public void ShowPending(ToolCall toolCall)
    {
        _call = toolCall;
        _titleBlock.Text = $"→ read {FormatInvocation(toolCall)}";
    }

    public void Complete(ToolResult result)
    {
        if (_call is null) throw new InvalidOperationException(
            "ReadToolCardView.Complete called before ShowPending.");
        var status = AvaloniaToolCardHelpers.StatusPrefix(result.IsError);
        _titleBlock.Text = $"{status} read {FormatInvocation(_call)} — {FormatSummary(result)}";
    }

    internal static string FormatInvocation(ToolCall toolCall)
    {
        var path = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "path");
        var offset = AvaloniaToolCardHelpers.TryGetInt(toolCall.Arguments, "offset");
        var limit = AvaloniaToolCardHelpers.TryGetInt(toolCall.Arguments, "limit");
        if (offset is null && limit is null) return path;
        var offsetText = offset?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "1";
        var limitText = limit is { } l ? l.ToString(System.Globalization.CultureInfo.InvariantCulture) : "all";
        return $"{path} [offset={offsetText}, limit={limitText}]";
    }

    private static string FormatSummary(ToolResult result)
    {
        var text = result.Text;
        return text.Length > 80 ? text[..77] + "…" : text;
    }
}

/// <summary>
/// Card for <c>write</c>: title carries the path + summary, body is empty
/// on success and the error text on failure.
/// </summary>
public sealed class WriteToolCardView : AvaloniaToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        var path = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "path");
        SetTitle($"→ write {path}");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var path = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "path");
        var status = AvaloniaToolCardHelpers.StatusPrefix(result.IsError);
        SetTitle($"{status} write {path} — {Truncate(result.Text)}");
        SetBody(result.IsError
            ? AvaloniaToolCardHelpers.BodyText(Truncate(result.Text))
            : null);
    }

    private static string Truncate(string text) =>
        text.Length > 240 ? text[..237] + "…" : text;
}

/// <summary>
/// Card for <c>edit</c>: title carries the path, body is the truncated
/// result text (a real diff view can replace this later).
/// </summary>
public sealed class EditToolCardView : AvaloniaToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        var path = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "path");
        SetTitle($"→ edit {path}");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var path = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "path");
        var status = AvaloniaToolCardHelpers.StatusPrefix(result.IsError);
        SetTitle($"{status} edit {path}");
        SetBody(AvaloniaToolCardHelpers.BodyText(Truncate(result.Text)));
    }

    private static string Truncate(string text) =>
        text.Length > 240 ? text[..237] + "…" : text;
}

/// <summary>
/// Card for <c>bash</c>: title is <c>$ command</c>, body is the truncated
/// output in a monospace font.
/// </summary>
public sealed class BashToolCardView : AvaloniaToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        var command = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "command");
        SetTitle($"$ {command}");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var command = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "command");
        var status = AvaloniaToolCardHelpers.StatusPrefix(result.IsError);
        SetTitle($"{status} $ {command}");
        SetBody(AvaloniaToolCardHelpers.BodyText(Truncate(result.Text), mono: true));
    }

    private static string Truncate(string text) =>
        text.Length > 400 ? text[..397] + "…" : text;
}

/// <summary>Fallback card for unknown tool names (MCP tools etc.).</summary>
public sealed class GenericToolCardView : AvaloniaToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        SetTitle($"→ {toolCall.Name}");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var status = AvaloniaToolCardHelpers.StatusPrefix(result.IsError);
        SetTitle($"{status} {toolCall.Name}");
        SetBody(AvaloniaToolCardHelpers.BodyText(Truncate(result.Text)));
    }

    private static string Truncate(string text) =>
        text.Length > 240 ? text[..237] + "…" : text;
}
