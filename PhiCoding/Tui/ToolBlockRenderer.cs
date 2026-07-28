using System.Text;
using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding.Tools.Details;

namespace PhiCoding.Tui;

/// <summary>
/// Renders one tool call's invocation line and result body for the TUI
/// transcript. Per-tool dispatch — reads the typed <c>Details</c> payload
/// emitted by each <see cref="PhiCoding.Tools.TypedTool{TArgs}"/> and
/// shapes the output accordingly (e.g. unified diff for edit).
/// </summary>
internal static class ToolBlockRenderer
{
    private const int ResultTruncateLength = 500;

    public static string RenderInvocation(string name, ToolCall call)
    {
        var args = call.Arguments;
        return name switch
        {
            "read" => $"→ read {GetString(args, "path")}",
            "write" => $"→ write {GetString(args, "path")}",
            "edit" => $"→ edit {GetString(args, "path")}",
            "bash" => $"$ {GetString(args, "command")}",
            _ => $"→ {name}",
        };
    }

    public static string RenderResult(string name, ToolResult result)
    {
        var body = name switch
        {
            "read" => RenderRead(result),
            "write" => RenderWrite(result),
            "edit" => RenderEdit(result),
            "bash" => RenderBash(result),
            _ => $"    {Truncate(result.Text, ResultTruncateLength)}",
        };

        var status = result.IsError ? "✗" : "✓";
        var firstLineEnd = body.IndexOf('\n');
        var first = firstLineEnd < 0 ? body : body[..firstLineEnd];
        var rest = firstLineEnd < 0 ? "" : body[firstLineEnd..];
        return $"  {status} {first}{rest}";
    }

    private static string RenderRead(ToolResult r)
    {
        if (ToolDetails.Read<ReadDetails>(r.Details) is { } d)
            return $"read — {d.LineCount} lines · {FormatBytes(d.ByteCount)}\n    {Truncate(r.Text, ResultTruncateLength)}";
        return $"    {Truncate(r.Text, ResultTruncateLength)}";
    }

    private static string RenderWrite(ToolResult r)
    {
        if (ToolDetails.Read<WriteDetails>(r.Details) is { } d)
            return $"write — {d.BytesWritten} bytes ({d.Mode})";
        return $"    {Truncate(r.Text, ResultTruncateLength)}";
    }

    private static string RenderEdit(ToolResult r)
    {
        if (ToolDetails.Read<EditDetails>(r.Details) is not { } d)
            return $"    {Truncate(r.Text, ResultTruncateLength)}";

        var sb = new StringBuilder();
        sb.AppendLine($"edit {d.Path}");
        foreach (var line in DiffFormatter.Parse(d.Patch))
            sb.AppendLine($"    {line.Text}");
        return sb.ToString().TrimEnd('\n', '\r');
    }

    private static string RenderBash(ToolResult r)
    {
        if (ToolDetails.Read<BashDetails>(r.Details) is { } d)
            return $"bash — exit={d.ExitCode} in {d.DurationMs}ms\n    {Truncate(r.Text, ResultTruncateLength)}";
        return $"    {Truncate(r.Text, ResultTruncateLength)}";
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

    private static string FormatBytes(int n) => n switch
    {
        < 1024 => $"{n}B",
        < 1024 * 1024 => $"{n / 1024.0:F1}KB",
        _ => $"{n / 1024.0 / 1024.0:F1}MB",
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}