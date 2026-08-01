using System.Globalization;
using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding.Tools.Details;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui;

/// <summary>
/// Pure formatting helpers for tool-call cards: invocation titles, result
/// summaries, truncated output previews and colored diff markup. The visual
/// layer (<see cref="ChatTranscript"/>) wraps these strings in controls.
/// All output that embeds tool text is escaped for ANSI markup.
/// </summary>
internal static class ToolCardRenderer
{
    internal const int PreviewMaxLines = 8;
    internal const int PreviewMaxChars = 2000;

    public static string FormatInvocation(ToolCall call)
    {
        var args = call.Arguments;
        return call.Name switch
        {
            "read" => FormatReadInvocation(args),
            "write" => $"→ write {GetString(args, "path")}",
            "edit" => $"→ edit {GetString(args, "path")}",
            "bash" => $"$ {GetString(args, "command")}",
            _ => $"→ {call.Name}",
        };
    }

    private static string FormatReadInvocation(JsonNode? args)
    {
        var path = GetString(args, "path");
        var offset = TryGetInt(args, "offset");
        var limit = TryGetInt(args, "limit");
        if (offset is null && limit is null) return $"→ read {path}";
        var offsetText = offset?.ToString(CultureInfo.InvariantCulture) ?? "1";
        var limitText = limit is { } l ? l.ToString(CultureInfo.InvariantCulture) : "all";
        // The range hint is author-controlled, fixed-shape text; XenoAtom
        // renders unknown markup tags like "[offset=10, limit=18]" literally,
        // so no escaping is needed here. (Escape stays for untrusted text.)
        return $"→ read {path} [offset={offsetText}, limit={limitText}]";
    }

    public static string FormatSummary(string name, ToolResult result) => name switch
    {
        "read" when ToolDetails.Read<ReadDetails>(result.Details) is { } d
            => FormatReadSummary(d),
        "write" when ToolDetails.Read<WriteDetails>(result.Details) is { } d
            => $"write — {d.BytesWritten} bytes ({d.Mode})",
        "edit" when ToolDetails.Read<EditDetails>(result.Details) is { } d
            => $"edit {d.Path} · {d.Edits.Count} block(s)",
        "bash" when ToolDetails.Read<BashDetails>(result.Details) is { } d
            => $"bash — exit={d.ExitCode} in {d.DurationMs}ms",
        _ => name,
    };

    private static string FormatReadSummary(ReadDetails d)
    {
        var slice = (d.Offset, d.Limit) is ( > 1, _) || d.LineCount < d.TotalLineCount;
        return slice
            ? $"read — lines {d.Offset}-{d.Offset + d.LineCount - 1} of {d.TotalLineCount} · {FormatBytes(d.ByteCount)}"
            : $"read — {d.TotalLineCount} lines · {FormatBytes(d.ByteCount)}";
    }

    /// <summary>
    /// Builds the result-body control for a completed tool call: a
    /// side-by-side <see cref="Grid"/> diff for successful edits, an
    /// empty Markup for successful reads, otherwise a truncated output
    /// preview (dim, or red on error).
    /// </summary>
    public static Visual FormatResultBody(string name, ToolResult result)
    {
        if (!result.IsError && name == "read")
            return new Markup("");

        if (!result.IsError && name == "edit"
            && ToolDetails.Read<EditDetails>(result.Details) is { } edit)
        {
            return SideBySideDiff.Build(edit);
        }

        var style = result.IsError ? "red" : "dim";
        var lines = TruncateLines(result.Text, PreviewMaxLines, PreviewMaxChars, out var hidden, out var charTruncated);
        var body = string.Join('\n', lines.Select(l => $"[{style}]{Escape(l)}[/]"));
        if (hidden > 0 || charTruncated)
        {
            var note = hidden > 0 ? $"{hidden} more lines" : "output";
            body += $"\n[dim]… ({note} hidden)[/]";
        }
        return new Markup(body) { Wrap = true };
    }

    public static string DiffLineToMarkup(DiffLine line) => line.Kind switch
    {
        DiffLineKind.Added => $"[green]{Escape(line.Text)}[/]",
        DiffLineKind.Removed => $"[red]{Escape(line.Text)}[/]",
        DiffLineKind.Context => Escape(line.Text),
        _ => $"[dim]{Escape(line.Text)}[/]",
    };

    public static IReadOnlyList<string> TruncateLines(
        string text, int maxLines, int maxChars, out int hiddenLines, out bool charTruncated)
    {
        var truncated = text.Length > maxChars ? text[..maxChars] : text;
        charTruncated = truncated.Length < text.Length;
        var all = truncated.Replace("\r\n", "\n").Split('\n');
        hiddenLines = Math.Max(0, all.Length - maxLines);
        return all.Length > maxLines ? all[..maxLines] : all;
    }

    public static string Escape(string text) => text.Replace("[", "\\[").Replace("]", "\\]");

    private static string GetString(JsonNode? args, string key)
    {
        if (args is JsonObject o
            && o.TryGetPropertyValue(key, out var v)
            && v is JsonValue jv
            && jv.TryGetValue<string>(out var s))
            return s;
        return "";
    }

    private static int? TryGetInt(JsonNode? args, string key)
    {
        if (args is not JsonObject o) return null;
        if (!o.TryGetPropertyValue(key, out var v)) return null;
        if (v is not JsonValue jv) return null;
        if (jv.TryGetValue<long>(out var n)) return (int)n;
        if (jv.TryGetValue<int>(out var i)) return i;
        return null;
    }

    private static string FormatBytes(int n) => n switch
    {
        < 1024 => $"{n}B",
        < 1024 * 1024 => $"{n / 1024.0:F1}KB",
        _ => $"{n / 1024.0 / 1024.0:F1}MB",
    };
}
