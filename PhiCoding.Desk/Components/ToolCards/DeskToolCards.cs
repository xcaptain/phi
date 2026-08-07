using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using PhiAgent;

namespace PhiCoding.Desk.Components.ToolCards;

/// <summary>
/// Default card shape: a header row (status + invocation) plus an
/// expandable body that <see cref="ShowPending"/> initializes as a
/// placeholder and <see cref="Complete"/> replaces with the tool-specific
/// body. <see cref="ReadToolCardView"/> overrides this with a single-line
/// layout because reads render no body.
/// </summary>
public abstract class DeskToolCardBase : IDeskToolCard, IDisposable
{
    private ToolCall? _call;
    private readonly ObservableValue<string> _title = new(string.Empty);
    private readonly ContentControl _bodyHolder = new();

    protected DeskToolCardBase()
    {
        var titleBlock = new Label()
            .BindText(_title)
            .FontWeight(FontWeight.SemiBold);

        var border = new Border()
            .Padding(8)
            .Margin(0, 0, 0, 4)
            .CornerRadius(6)
            .WithTheme((t, b) =>
            {
                b.Background(t.Palette.ContainerBackground);
                b.BorderBrush(t.Palette.ControlBorder);
            })
            .BorderThickness(1)
            .Child(
                new StackPanel()
                    .Orientation(Aprillz.MewUI.Orientation.Vertical)
                    .Spacing(4)
                    .Children(titleBlock, _bodyHolder));
        Visual = border;
    }

    public FrameworkElement Visual { get; }

    public void ShowPending(ToolCall toolCall)
    {
        _call = toolCall;
        OnShowPending(toolCall);
        _bodyHolder.Content = new Label().Text("…").WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t)));
    }

    public void Complete(ToolResult result)
    {
        var toolCall = _call ?? throw new InvalidOperationException(
            $"{GetType().Name}.Complete called before ShowPending.");
        OnComplete(toolCall, result);
    }

    protected void SetTitle(string text) => _title.Value = text;
    protected void SetBody(FrameworkElement? body) => _bodyHolder.Content = body;

    /// <summary>Disposes the card's visual tree (MewUI elements are disposable).</summary>
    public void Dispose()
    {
        Visual.Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract void OnShowPending(ToolCall toolCall);
    protected abstract void OnComplete(ToolCall toolCall, ToolResult result);
}

/// <summary>
/// One-line card for <c>read</c>: no body, no placeholder, just a title
/// that flips from dim invocation to status+invocation+summary when the
/// result lands.
/// </summary>
public sealed class ReadToolCardView : IDeskToolCard, IDisposable
{
    private readonly ObservableValue<string> _title = new(string.Empty);
    private ToolCall? _call;

    public ReadToolCardView()
    {
        Visual = new Label()
            .BindText(_title);
    }

    public FrameworkElement Visual { get; }

    /// <summary>Disposes the card's visual tree.</summary>
    public void Dispose()
    {
        Visual.Dispose();
        GC.SuppressFinalize(this);
    }

    public void ShowPending(ToolCall toolCall)
    {
        _call = toolCall;
        _title.Value = $"→ read {FormatInvocation(toolCall)}";
    }

    public void Complete(ToolResult result)
    {
        if (_call is null) throw new InvalidOperationException(
            "ReadToolCardView.Complete called before ShowPending.");
        var status = DeskToolCardHelpers.StatusPrefix(result.IsError);
        _title.Value = $"{status} read {FormatInvocation(_call)} — {FormatSummary(result)}";
    }

    internal static string FormatInvocation(ToolCall toolCall)
    {
        var path = DeskToolCardHelpers.GetString(toolCall.Arguments, "path");
        var offset = DeskToolCardHelpers.TryGetInt(toolCall.Arguments, "offset");
        var limit = DeskToolCardHelpers.TryGetInt(toolCall.Arguments, "limit");
        if (offset is null && limit is null) return path;
        var offsetText = offset?.ToString(CultureInfo.InvariantCulture) ?? "1";
        var limitText = limit is { } l ? l.ToString(CultureInfo.InvariantCulture) : "all";
        return $"{path} [offset={offsetText}, limit={limitText}]";
    }

    private static string FormatSummary(ToolResult result)
    {
        // ToolDetails.Read<ReadDetails> lives in PhiCoding.Tools.Details;
        // the Desk treats the result text alone as a fallback when the
        // typed details aren't surfaced.
        var text = result.Text;
        return text.Length > 80 ? text[..77] + "…" : text;
    }
}

/// <summary>
/// Card for <c>write</c>: title carries the path + bytes/mode summary,
/// body is empty on success and the error text on failure.
/// </summary>
public sealed class WriteToolCardView : DeskToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        var path = DeskToolCardHelpers.GetString(toolCall.Arguments, "path");
        SetTitle($"→ write {path}");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var path = DeskToolCardHelpers.GetString(toolCall.Arguments, "path");
        var status = DeskToolCardHelpers.StatusPrefix(result.IsError);
        SetTitle($"{status} write {path} — {Truncate(result.Text)}");
        SetBody(result.IsError
            ? new Label().Text(Truncate(result.Text))
            : null);
    }

    private static string Truncate(string text) =>
        text.Length > 240 ? text[..237] + "…" : text;
}

/// <summary>
/// Card for <c>edit</c>: title carries the path + block count, body is a
/// side-by-side diff grid on success and the error text on failure.
/// Diff construction is delegated to a future <c>SideBySideDiffView</c>
/// helper — for now the body simply carries the new content text.
/// </summary>
public sealed class EditToolCardView : DeskToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        var path = DeskToolCardHelpers.GetString(toolCall.Arguments, "path");
        SetTitle($"→ edit {path}");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var path = DeskToolCardHelpers.GetString(toolCall.Arguments, "path");
        var status = DeskToolCardHelpers.StatusPrefix(result.IsError);
        SetTitle($"{status} edit {path}");
        SetBody(new Label().Text(Truncate(result.Text)));
    }

    private static string Truncate(string text) =>
        text.Length > 240 ? text[..237] + "…" : text;
}

/// <summary>
/// Card for <c>bash</c>: title is <c>$ command</c> + exit/duration summary,
/// body is the truncated output (dim on success, red on error).
/// </summary>
public sealed class BashToolCardView : DeskToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        var command = DeskToolCardHelpers.GetString(toolCall.Arguments, "command");
        SetTitle($"$ {command}");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var command = DeskToolCardHelpers.GetString(toolCall.Arguments, "command");
        var status = DeskToolCardHelpers.StatusPrefix(result.IsError);
        SetTitle($"{status} $ {command}");
        SetBody(new Label()
            .Text(Truncate(result.Text))
            .FontFamily("Consolas")
            .TextWrapping(TextWrapping.Wrap)
            .WithTheme((t, c) => c.Foreground(DeskTheme.TextSecondary(t))));
    }

    private static string Truncate(string text) =>
        text.Length > 400 ? text[..397] + "…" : text;
}

/// <summary>Fallback card for unknown tool names (MCP tools etc.).</summary>
public sealed class GenericToolCardView : DeskToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        SetTitle($"→ {toolCall.Name}");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var status = DeskToolCardHelpers.StatusPrefix(result.IsError);
        SetTitle($"{status} {toolCall.Name}");
        SetBody(new Label().Text(Truncate(result.Text)));
    }

    private static string Truncate(string text) =>
        text.Length > 240 ? text[..237] + "…" : text;
}