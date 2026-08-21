using System.Globalization;
using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Tools.Details;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Phi.Tui.Components.ToolCards;

/// <summary>
/// Single-line card for <c>read</c>: no body, no placeholder, just a title that
/// flips from dim invocation to status+invocation+summary when the result lands.
/// Differs from <see cref="ToolCardBase"/>'s title/body shape because the user
/// already sees the file content in the chat above; a second body box is noise.
/// </summary>
public sealed class ReadToolCard : IToolCard
{
    private readonly State<string> _title = new("");
    private ToolCall? _call;

    public ReadToolCard()
    {
        Visual = new Markup(() => _title.Value)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Start,
        };
    }

    public Visual Visual { get; }

    public ToolCall? Call => _call;

    public string Title => _title.Value;

    public void ShowPending(ToolCall toolCall)
    {
        _call = toolCall;
        // FormatInvocation produces author-controlled text including the
        // literal "[offset=N, limit=M]" hint — those brackets are markup-
        // inert (unknown tags render as text) and must NOT be escaped, so we
        // embed them raw into the dim wrapper.
        _title.Value = $"[dim]{FormatInvocation(toolCall)}[/]";
    }

    public void Complete(ToolResult result)
    {
        if (_call is null) throw new InvalidOperationException(
            "ReadToolCard.Complete called before ShowPending.");
        var status = result.IsError ? "[red]✗[/]" : "[green]✓[/]";
        _title.Value = $"{status} [primary]{FormatInvocation(_call)}[/] [dim]· {ToolCardBase.Escape(FormatSummary(result))}[/]";
    }

    internal static string FormatInvocation(ToolCall call)
    {
        var path = GetString(call.Arguments, "path");
        var offset = TryGetInt(call.Arguments, "offset");
        var limit = TryGetInt(call.Arguments, "limit");
        if (offset is null && limit is null) return $"→ read {path}";
        var offsetText = offset?.ToString(CultureInfo.InvariantCulture) ?? "1";
        var limitText = limit is { } l ? l.ToString(CultureInfo.InvariantCulture) : "all";
        // The range hint is author-controlled, fixed-shape text; XenoAtom
        // renders unknown markup tags like "[offset=10, limit=18]" literally,
        // so no escaping is needed here.
        return $"→ read {path} [offset={offsetText}, limit={limitText}]";
    }

    private static string FormatSummary(ToolResult result)
    {
        if (ToolDetails.Read<ReadDetails>(result.Details) is not { } d)
            return "read";
        var slice = (d.Offset, d.Limit) is ( > 1, _) || d.LineCount < d.TotalLineCount;
        return slice
            ? $"read — lines {d.Offset}-{d.Offset + d.LineCount - 1} of {d.TotalLineCount} · {FormatBytes(d.ByteCount)}"
            : $"read — {d.TotalLineCount} lines · {FormatBytes(d.ByteCount)}";
    }

    private static string FormatBytes(int n) => n switch
    {
        < 1024 => $"{n}B",
        < 1024 * 1024 => $"{n / 1024.0:F1}KB",
        _ => $"{n / 1024.0 / 1024.0:F1}MB",
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

    private static int? TryGetInt(JsonNode? args, string key)
    {
        if (args is not JsonObject o) return null;
        if (!o.TryGetPropertyValue(key, out var v)) return null;
        if (v is not JsonValue jv) return null;
        if (jv.TryGetValue<long>(out var n)) return (int)n;
        if (jv.TryGetValue<int>(out var i)) return i;
        return null;
    }
}
