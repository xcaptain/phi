using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Media;
using PhiAgent;
using PhiCoding.Tools.Details;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace PhiCoding.Avalonia.Components.ToolCards;

/// <summary>
/// Per-tool card on the desktop side. The card visualizes one
/// <see cref="PhiCoding.Chat.ToolCallLine"/> as a unified
/// header + scrollable-detail layout:
/// <list type="bullet">
/// <item>Header (always visible, clickable): <c>{status} {tool}:
/// {summary}</c> — e.g. <c>✓ Bash: grep 'xx' abc.cs</c> or <c>✓ read:
/// aaa.cs [offset=90, limit=200]</c>.</item>
/// <item>Detail body (visible when expanded): a
/// <see cref="ToolCardBodyFrame"/>-wrapped content Control built per
/// tool. The frame caps the rendered height so long output scrolls
/// instead of blowing up the transcript.</item>
/// </list>
/// Pending state shows a <c>›</c> glyph and a placeholder body;
/// <see cref="Complete"/> swaps both via the
/// <c>SetHeader</c> + <c>SetDetailBody</c> helpers on
/// <see cref="AvaloniaToolCardBase"/>.
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

/// <summary>Shared helpers: JSON argument lookup, status prefixes, body text.</summary>
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

    /// <summary>"✓" / "✗" status glyph for the header line.</summary>
    public static string StatusGlyph(bool isError) => isError ? "✗" : "✓";

    /// <summary>"›" pending glyph — drawn before any result has arrived.</summary>
    public const string PendingGlyph = "›";

    /// <summary>
    /// Builds the canonical <c>{status} {name}: {summary}</c> header
    /// string. When <paramref name="summary"/> is empty the colon and
    /// summary are dropped (tool name still appears).
    /// </summary>
    public static string FormatHeader(string statusGlyph, string name, string summary) =>
        string.IsNullOrEmpty(summary)
            ? $"{statusGlyph} {name}"
            : $"{statusGlyph} {name}: {summary}";

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
/// Base class for every tool card: wraps a <see cref="CollapsibleSection"/>
/// whose header is the unified summary line and whose body is a
/// <see cref="ToolCardBodyFrame"/>-wrapped detail view. Subclasses
/// implement <see cref="OnShowPending"/> + <see cref="OnComplete"/> to
/// populate <see cref="SetHeader"/> + <see cref="SetDetailBody"/>.
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
            new TextBlock { Text = "…" },
            startExpanded: false);
    }

    public Control Visual => _section;

    /// <summary>The <see cref="ToolCall"/> the card is rendering.</summary>
    protected ToolCall? Call => _call;

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

    /// <summary>Sets the header line. Format: <c>{status} {name}:
    /// {summary}</c> (or <c>{status} {name}</c> when summary is empty).</summary>
    protected void SetHeader(string statusGlyph, string name, string summary) =>
        _titleBlock.Text = AvaloniaToolCardHelpers.FormatHeader(statusGlyph, name, summary);

    /// <summary>
    /// Sets the detail body, wrapped in the standard
    /// <see cref="ToolCardBodyFrame"/>. Pass <c>null</c> to render no
    /// detail (header summary is enough — e.g. successful write).
    /// </summary>
    protected void SetDetailBody(Control? body)
    {
        if (body is null)
            _section.SetBody(new TextBlock { Text = string.Empty });
        else
            _section.SetBody(new ToolCardBodyFrame(body));
    }

    protected abstract void OnShowPending(ToolCall toolCall);
    protected abstract void OnComplete(ToolCall toolCall, ToolResult result);
}

/// <summary>
/// <c>read</c> card: header is <c>✓ read: &lt;path&gt; [offset=N, limit=M]</c>,
/// body is a <see cref="SyntaxHighlightedContent"/> with a metadata line
/// and per-extension syntax highlighting (markdown code block for known
/// extensions, mono fallback otherwise). Defaults to collapsed — the user
/// clicks to see the file body.
/// </summary>
public sealed class ReadToolCardView : AvaloniaToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        SetHeader(
            AvaloniaToolCardHelpers.PendingGlyph,
            "read",
            FormatInvocation(toolCall));
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var status = AvaloniaToolCardHelpers.StatusGlyph(result.IsError);
        SetHeader(status, "read", FormatInvocation(Call!));

        if (result.IsError)
        {
            SetDetailBody(AvaloniaToolCardHelpers.BodyText(result.Text,
                foreground: AvaloniaTheme.Danger));
            return;
        }

        var details = ToolDetails.Read<ReadDetails>(result.Details);
        var path = details?.Path
            ?? AvaloniaToolCardHelpers.GetString(Call!.Arguments, "path");
        var lang = SyntaxHighlightedContent.DetectLanguage(path);

        // Strip ReadTool's continuation hint — when read truncates, it
        // appends "[N more lines... use offset=X]". The metadata header
        // we render above already conveys this info more cleanly, so the
        // hint would just duplicate it inside the code block.
        var content = StripContinuationHint(result.Text);

        var metaHeader = details is null
            ? path
            : $"{path}  ·  lines {details.Offset}-{details.Offset + details.LineCount - 1} of {details.TotalLineCount}  ·  {AvaloniaToolCardHelpers.FormatBytes(details.ByteCount)}";

        SetDetailBody(new SyntaxHighlightedContent(metaHeader, content, lang));
    }

    internal static string FormatInvocation(ToolCall toolCall)
    {
        var path = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "path");
        var offset = AvaloniaToolCardHelpers.TryGetInt(toolCall.Arguments, "offset");
        var limit = AvaloniaToolCardHelpers.TryGetInt(toolCall.Arguments, "limit");
        if (offset is null && limit is null) return path;
        var offsetText = offset?.ToString(CultureInfo.InvariantCulture) ?? "1";
        var limitText = limit is { } l ? l.ToString(CultureInfo.InvariantCulture) : "all";
        return $"{path} [offset={offsetText}, limit={limitText}]";
    }

    /// <summary>
    /// Removes the <c>[Output truncated...]</c> / <c>[N more lines...]</c>
    /// hints ReadTool appends when the result was sliced. The metadata
    /// header above the code block already carries the slice info, so the
    /// hint would just appear inside the code block as noise.
    /// </summary>
    private static string StripContinuationHint(string text)
    {
        var idx = text.IndexOf("\n\n[", StringComparison.Ordinal);
        return idx > 0 ? text[..idx] : text;
    }
}

/// <summary>
/// <c>write</c> card: header is <c>✓ write: &lt;path&gt; · N bytes · &lt;mode&gt;</c>.
/// On success the body is a 3-line mono metadata block (path / bytes /
/// mode). On failure the body is the error text. Either way the body
/// sits inside the standard <see cref="ToolCardBodyFrame"/> so it
/// behaves like every other tool's detail view.
/// </summary>
public sealed class WriteToolCardView : AvaloniaToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        var path = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "path");
        SetHeader(AvaloniaToolCardHelpers.PendingGlyph, "write", path);
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var details = ToolDetails.Read<WriteDetails>(result.Details);
        var status = AvaloniaToolCardHelpers.StatusGlyph(result.IsError);
        var path = details?.Path
            ?? AvaloniaToolCardHelpers.GetString(Call!.Arguments, "path");
        // Header summary already names the file + size; surface the
        // created-vs-overwrote mode so the user can spot replays of
        // writes that clobbered an existing file.
        var summary = details is null
            ? path
            : $"{path}  ·  {details.BytesWritten.ToString("N0", CultureInfo.InvariantCulture)} bytes  ·  {details.Mode}";
        SetHeader(status, "write", summary);

        if (result.IsError)
        {
            SetDetailBody(AvaloniaToolCardHelpers.BodyText(result.Text,
                mono: true,
                foreground: AvaloniaTheme.Danger));
            return;
        }

        SetDetailBody(BuildMetadataBody(details));
    }

    private static TextBlock BuildMetadataBody(WriteDetails? details)
    {
        var text = details is null
            ? "(no metadata)"
            : $"path:  {details.Path}\n" +
              $"bytes: {details.BytesWritten.ToString("N0", CultureInfo.InvariantCulture)}\n" +
              $"mode:  {details.Mode}";
        return new TextBlock
        {
            Text = text,
            FontFamily = AvaloniaTheme.MonoFontFamily,
            Foreground = AvaloniaTheme.TextPrimary,
            TextWrapping = TextWrapping.NoWrap,
        };
    }
}

/// <summary>
/// <c>edit</c> card: header is <c>✓ edit: &lt;path&gt;</c> (or
/// <c>✗ edit: &lt;path&gt;</c> on failure). On success the body is the
/// existing <see cref="SideBySideDiff"/> grid; on failure it's the
/// error text. Both are wrapped in the standard
/// <see cref="ToolCardBodyFrame"/> so they scroll / cap like every
/// other tool's detail.
/// </summary>
public sealed class EditToolCardView : AvaloniaToolCardBase
{
    protected override void OnShowPending(ToolCall toolCall)
    {
        var path = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "path");
        SetHeader(AvaloniaToolCardHelpers.PendingGlyph, "edit", path);
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var edit = ToolDetails.Read<EditDetails>(result.Details);
        var path = edit?.Path
            ?? AvaloniaToolCardHelpers.GetString(Call!.Arguments, "path");
        var status = AvaloniaToolCardHelpers.StatusGlyph(result.IsError);
        var blockSummary = edit is not null ? $"{edit.Edits.Count} block(s)" : null;
        var summary = blockSummary is null ? path : $"{path}  ·  {blockSummary}";
        SetHeader(status, "edit", summary);

        if (!result.IsError && edit is not null)
            SetDetailBody(SideBySideDiff.Build(edit));
        else
            SetDetailBody(AvaloniaToolCardHelpers.BodyText(
                Truncate(result.Text),
                foreground: result.IsError ? AvaloniaTheme.Danger : null));
    }

    private static string Truncate(string text) =>
        text.Length > 240 ? text[..237] + "…" : text;
}

/// <summary>
/// <c>bash</c> card: header is <c>✓ Bash: &lt;command&gt;</c> (long
/// commands are truncated). Body is the simplified
/// <see cref="BashOutputView"/> (stdout + stderr mono blocks only —
/// the command itself lives in the header). When
/// <see cref="BashDetails"/> is missing (legacy transcripts) the body
/// falls back to parsing <see cref="ToolResult.Content"/>'s
/// <see cref="TextBlock"/>s by the BashTool emit convention
/// (<c>[TextBlock(stdout), TextBlock(stderr)]</c>).
/// </summary>
public sealed class BashToolCardView : AvaloniaToolCardBase
{
    private const int CommandSummaryMaxChars = 80;

    protected override void OnShowPending(ToolCall toolCall)
    {
        var command = AvaloniaToolCardHelpers.GetString(toolCall.Arguments, "command");
        SetHeader(AvaloniaToolCardHelpers.PendingGlyph, "Bash", SummarizeCommand(command));
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var bash = ToolDetails.Read<BashDetails>(result.Details);
        var command = bash?.Command
            ?? AvaloniaToolCardHelpers.GetString(Call!.Arguments, "command");
        var status = AvaloniaToolCardHelpers.StatusGlyph(result.IsError);
        SetHeader(status, "Bash", SummarizeCommand(command));

        var stdout = bash?.Stdout ?? ExtractText(result.Content, 0);
        var stderr = bash?.Stderr ?? ExtractText(result.Content, 1);
        SetDetailBody(new BashOutputView(stdout, stderr));
    }

    /// <summary>
    /// Truncates a long shell command for use as the collapsed-header
    /// summary. Newlines are folded into spaces so the title stays a
    /// single line.
    /// </summary>
    private static string SummarizeCommand(string command)
    {
        if (string.IsNullOrEmpty(command)) return "(no command)";
        var flat = command.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length > CommandSummaryMaxChars
            ? flat[..(CommandSummaryMaxChars - 1)] + "…"
            : flat;
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
        SetHeader(AvaloniaToolCardHelpers.PendingGlyph, toolCall.Name, "");
    }

    protected override void OnComplete(ToolCall toolCall, ToolResult result)
    {
        var status = AvaloniaToolCardHelpers.StatusGlyph(result.IsError);
        var summary = FormatArgSummary(Call!.Arguments);
        SetHeader(status, toolCall.Name, summary);
        SetDetailBody(AvaloniaToolCardHelpers.BodyText(Truncate(result.Text)));
    }

    /// <summary>
    /// Best-effort summary from the raw tool call arguments. Falls back
    /// to "(args)" when the args object is empty so the header line is
    /// not visually broken.
    /// </summary>
    private static string FormatArgSummary(JsonNode? args)
    {
        if (args is not JsonObject o || o.Count == 0) return "(args)";
        return string.Join(", ",
            o.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static string Truncate(string text) =>
        text.Length > 240 ? text[..237] + "…" : text;
}
