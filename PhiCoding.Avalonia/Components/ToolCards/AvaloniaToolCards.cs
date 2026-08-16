using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Media;
using PhiAgent;
using PhiCoding.Tools.Details;
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
            FontFamily = mono ? AvaloniaTheme.MonoFontFamily : FontFamily.Default,
            Foreground = foreground ?? AvaloniaTheme.TextSecondary,
        };
}

/// <summary>
/// Default card shape: a collapsible <see cref="CollapsibleSection"/> whose
/// header carries the status + invocation and whose body starts as a
/// placeholder ("…") on <see cref="ShowPending"/>. <see cref="Complete"/>
/// swaps the body via <see cref="SetBody"/> to a tool-specific summary.
/// </summary>
public abstract class AvaloniaToolCardBase : IAvaloniaToolCard
{
    private ToolCall? _call;
    private readonly TextBlock _titleBlock;
    private readonly CollapsibleSection _section;

    protected AvaloniaToolCardBase()
    {
        _titleBlock = new TextBlock { FontWeight = FontWeight.SemiBold };
        _section = new CollapsibleSection(
            _titleBlock,
            new TextBlock { Text = "…", Foreground = AvaloniaTheme.TextSecondary },
            startExpanded: false);
    }

    public Control Visual => _section;

    public void ShowPending(ToolCall toolCall)
    {
        _call = toolCall;
        OnShowPending(toolCall);
    }

    public void Complete(ToolResult result)
    {
        var toolCall = _call ?? throw new InvalidOperationException(
            $"{GetType().Name}.Complete called before ShowPending.");
        OnComplete(toolCall, result);
    }

    protected void SetTitle(string text) => _titleBlock.Text = text;
    protected void SetBody(Control? body)
    {
        // Tool cards can complete with no body (success summaries that fit
        // on the title row). Swap to an empty placeholder so the chevron
        // still indicates "details available" stays consistent.
        _section.SetBody(body
            ?? new TextBlock { Text = string.Empty });
    }

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
/// Card for <c>edit</c>: title carries the path + block count summary, body
/// is a <see cref="SideBySideDiff"/> grid on success and a truncated red
/// error body on failure. Diff construction is delegated to
/// <see cref="SideBySideDiff"/> because it owns the DiffPlex plumbing.
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
        var edit = ToolDetails.Read<EditDetails>(result.Details);
        var path = edit?.Path ?? AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "path");
        var status = AvaloniaToolCardHelpers.StatusPrefix(result.IsError);
        var summary = edit is not null ? $"{edit.Edits.Count} block(s)" : null;
        SetTitle(summary is null
            ? $"{status} edit {path}"
            : $"{status} edit {path}  ·  {summary}");

        if (!result.IsError && edit is not null)
            SetBody(SideBySideDiff.Build(edit));
        else
            SetBody(AvaloniaToolCardHelpers.BodyText(
                Truncate(result.Text),
                foreground: result.IsError ? AvaloniaTheme.Danger : null));
    }

    private static string Truncate(string text) =>
        text.Length > 240 ? text[..237] + "…" : text;
}

/// <summary>
/// Card for <c>bash</c>: title is just <c>✓ Bash 169 ms</c> / <c>✗ Bash 169 ms</c>
/// (name + duration badge — the full command is moved into the expanded
/// body as its own code-style row). Body shows the command at the top
/// with a copy button and the stdout / stderr split below via
/// <see cref="BashOutputView"/>.
/// <para>
/// When <see cref="BashDetails"/> is present (newer sessions) the body
/// pulls stdout / stderr / command straight from it. When Details is
/// missing (legacy transcripts written before persistence was added), the
/// body falls back to parsing <see cref="ToolResult.Content"/>'s
/// <see cref="TextBlock"/>s — <c>BashTool</c> emits the result as
/// <c>[TextBlock(stdout), TextBlock(stderr)]</c> so we read those slots by
/// convention. Either path produces the same rich body.
/// </para>
/// </summary>
public sealed class BashToolCardView : AvaloniaToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        SetTitle("Bash");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var bash = ToolDetails.Read<BashDetails>(result.Details);
        var status = AvaloniaToolCardHelpers.StatusPrefix(result.IsError);
        // Title mirrors the maka design: just the tool name + duration
        // (or exit-code / "running" while pending). Full command is in
        // the body so the title doesn't blow out on long pipelines.
        var durationText = bash is not null
            ? $"{bash.DurationMs}ms"
            : result.IsError ? "exit" : "ok";
        SetTitle($"{status} Bash {durationText}");

        // Pull stdout / stderr / command from the best available source.
        // BashDetails is preferred; legacy transcripts (no Details)
        // fall back to Content textblocks.
        var stdout = bash?.Stdout ?? ExtractText(result.Content, 0);
        var stderr = bash?.Stderr ?? ExtractText(result.Content, 1);
        var command = bash?.Command
            ?? AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "command");

        SetBody(new BashOutputView(command, stdout, stderr));
    }

    /// <summary>
    /// Returns the text of the <see cref="TextBlock"/> at
    /// <paramref name="index"/> in <paramref name="content"/>, or empty
    /// when missing. Used as the legacy fallback when
    /// <see cref="BashDetails"/> isn't persisted.
    /// </summary>
    private static string ExtractText(IReadOnlyList<ContentBlock> content, int index) =>
        content.OfType<PhiAgent.TextBlock>().ElementAtOrDefault(index)?.Text ?? "";
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
