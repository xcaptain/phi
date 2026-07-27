using PhiAgent;

namespace PhiCoding.Tui;

/// <summary>
/// Formats <see cref="HarnessEvent"/>s into display strings for the TUI.
/// Pure function — easy to unit test.
/// </summary>
internal static class EventFormatter
{
    private const int ToolResultTruncateLength = 500;

    public static string Format(HarnessEvent ev) => ev switch
    {
        TurnStartEvent ts => $"\n── turn {ts.Turn} ──\n",
        AssistantTextDeltaEvent t => t.Delta,
        AssistantToolCallEvent tc => $"\n[tool] {tc.ToolCall.Name}({tc.ToolCall.Id})\n",
        ToolExecutionStartEvent tes => $"  ↳ running {tes.ToolName}...",
        ToolExecutionEndEvent tee => $"  ✓ {Truncate(tee.Result.Text, ToolResultTruncateLength)}\n",
        TurnEndEvent te => $"\n[stop: {te.FinalMessage.StopReason}]\n",
        HarnessErrorEvent he => $"\n[error] {he.Message}\n",
        _ => "",
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}