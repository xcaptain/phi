using System.Text.Json.Nodes;
using Aprillz.MewUI.Controls;
using PhiAgent;
using PhiCoding.Chat;

namespace PhiCoding.Desk.Components.ToolCards;

/// <summary>
/// Per-tool card on the desktop side. The card visualizes one
/// <see cref="ToolCallLine"/>: a title row (status + invocation) plus an
/// optional body. Pending state shows a placeholder body; on
/// <see cref="Complete"/> the body swaps to a tool-specific summary
/// (diff grid for edits, output preview for bash, etc.).
/// </summary>
public interface IDeskToolCard
{
    FrameworkElement Visual { get; }
    void ShowPending(ToolCall toolCall);
    void Complete(ToolResult result);
}

/// <summary>
/// Resolves the <see cref="IDeskToolCard"/> implementation for a given
/// tool name. Adding a new tool means writing one <see cref="DeskToolCardBase"/>
/// subclass and adding a switch arm here — same shape as the TUI's
/// <c>ToolCardRegistry</c>.
/// </summary>
public static class DeskToolCardRegistry
{
    public static IDeskToolCard For(string name) => name switch
    {
        "read" => new ReadToolCardView(),
        "write" => new WriteToolCardView(),
        "edit" => new EditToolCardView(),
        "bash" => new BashToolCardView(),
        _ => new GenericToolCardView(),
    };
}

/// <summary>Shared helpers: JSON argument lookup + status prefixes.</summary>
internal static class DeskToolCardHelpers
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
        if (v is not JsonValue jv) return null;
        if (jv.TryGetValue<long>(out var n)) return (int)n;
        if (jv.TryGetValue<int>(out var i)) return i;
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
}