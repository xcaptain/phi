using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding.Tools.Details;

namespace PhiCoding.Tui;

/// <summary>
/// Renders one tool call's invocation line and result body as styled
/// transcript lines. Per-tool dispatch — reads the typed <c>Details</c>
/// payload emitted by each <see cref="PhiCoding.Tools.TypedTool{TArgs}"/>
/// (e.g. unified diff for edit, rendered with per-line diff styles).
/// </summary>
internal static class ToolBlockRenderer
{
    private const int PreviewMaxLines = 8;
    private const int PreviewMaxChars = 2000;

    public static List<TranscriptLine> RenderInvocationLines(ToolCall call)
    {
        var args = call.Arguments;
        var text = call.Name switch
        {
            "read" => $"→ read {GetString(args, "path")}",
            "write" => $"→ write {GetString(args, "path")}",
            "edit" => $"→ edit {GetString(args, "path")}",
            "bash" => $"$ {GetString(args, "command")}",
            _ => $"→ {call.Name}",
        };
        return [new TranscriptLine(text, TranscriptStyle.ToolCall)];
    }

    public static List<TranscriptLine> RenderResultLines(string name, ToolResult result)
    {
        var style = result.IsError ? TranscriptStyle.ToolError : TranscriptStyle.ToolOk;
        var status = result.IsError ? "✗" : "✓";
        var summary = Summarize(name, result);
        var lines = new List<TranscriptLine> { new($"  {status} {summary}", style) };

        if (!result.IsError && name == "edit" && ToolDetails.Read<EditDetails>(result.Details) is { } edit)
        {
            foreach (var line in DiffFormatter.Parse(edit.Patch))
                lines.Add(new TranscriptLine($"    {line.Text}", DiffStyle(line.Kind)));
            return lines;
        }

        lines.AddRange(PreviewLines(result.Text, result.IsError));
        return lines;
    }

    private static string Summarize(string name, ToolResult result) => name switch
    {
        "read" when ToolDetails.Read<ReadDetails>(result.Details) is { } d
            => $"read — {d.LineCount} lines · {FormatBytes(d.ByteCount)}",
        "write" when ToolDetails.Read<WriteDetails>(result.Details) is { } d
            => $"write — {d.BytesWritten} bytes ({d.Mode})",
        "edit" when ToolDetails.Read<EditDetails>(result.Details) is { } d
            => $"edit {d.Path}",
        "bash" when ToolDetails.Read<BashDetails>(result.Details) is { } d
            => $"bash — exit={d.ExitCode} in {d.DurationMs}ms",
        _ => name,
    };

    private static IEnumerable<TranscriptLine> PreviewLines(string text, bool isError)
    {
        var style = isError ? TranscriptStyle.ToolError : TranscriptStyle.ToolOutput;
        var truncated = text.Length > PreviewMaxChars ? text[..PreviewMaxChars] : text;
        var all = truncated.Replace("\r\n", "\n").Split('\n');
        var shown = all.Length > PreviewMaxLines ? all[..PreviewMaxLines] : all;
        foreach (var line in shown)
            yield return new TranscriptLine($"    {line}", style);
        var hidden = all.Length - shown.Length;
        if (hidden > 0 || truncated.Length < text.Length)
            yield return new TranscriptLine(
                $"    … ({(hidden > 0 ? $"{hidden} more lines" : "output")} hidden)", TranscriptStyle.DiffMeta);
    }

    private static TranscriptStyle DiffStyle(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added => TranscriptStyle.DiffAdded,
        DiffLineKind.Removed => TranscriptStyle.DiffRemoved,
        DiffLineKind.Context => TranscriptStyle.Default,
        _ => TranscriptStyle.DiffMeta,
    };

    private static string GetString(JsonNode? args, string key)
    {
        if (args is JsonObject o
            && o.TryGetPropertyValue(key, out var v)
            && v is JsonValue jv
            && jv.TryGetValue<string>(out var s))
            return s;
        return "";
    }

    private static string FormatBytes(int n) => n switch
    {
        < 1024 => $"{n}B",
        < 1024 * 1024 => $"{n / 1024.0:F1}KB",
        _ => $"{n / 1024.0 / 1024.0:F1}MB",
    };
}
