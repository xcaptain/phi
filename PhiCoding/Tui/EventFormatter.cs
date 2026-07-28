using PhiAgent;

namespace PhiCoding.Tui;

/// <summary>
/// Formats <see cref="HarnessEvent"/>s into display strings for the TUI.
/// Pure function — easy to unit test. Tool-specific rendering is delegated
/// to <see cref="ToolBlockRenderer"/>.
/// </summary>
internal static class EventFormatter
{
    public static string Format(HarnessEvent ev) => ev switch
    {
        TurnStartEvent ts => $"\n── turn {ts.Turn} ──\n",
        AssistantTextDeltaEvent t => t.Delta,
        AssistantToolCallEvent tc => $"\n{ToolBlockRenderer.RenderInvocation(tc.ToolCall.Name, tc.ToolCall)}\n",
        ToolExecutionStartEvent tes => $"  ↳ running {tes.ToolName}...",
        ToolExecutionEndEvent tee =>
            $"\n{ToolBlockRenderer.RenderResult(tee.ToolCall.Name, tee.Result)}\n",
        TurnEndEvent te => $"\n[stop: {te.FinalMessage.StopReason}]\n",
        HarnessErrorEvent he => $"\n[error] {he.Message}\n",
        _ => "",
    };
}