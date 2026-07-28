using PhiAgent;

namespace PhiCoding.Tui;

/// <summary>
/// Translates <see cref="HarnessEvent"/>s into <see cref="TuiState"/>
/// mutations (tau's TuiEventAdapter equivalent). Pure projection — no UI
/// dependencies, fully unit-testable.
/// </summary>
internal static class TuiEventAdapter
{
    public static void Apply(TuiState state, HarnessEvent ev)
    {
        switch (ev)
        {
            case TurnStartEvent ts:
                state.BeginTurn(ts.Turn);
                break;
            case AssistantTextDeltaEvent d:
                state.AppendAssistantDelta(d.Delta);
                break;
            case AssistantToolCallEvent tc:
                state.AddToolCall(tc.ToolCall);
                break;
            case ToolExecutionStartEvent:
                // Invocation row already exists from AssistantToolCallEvent;
                // nothing to render until the result arrives.
                break;
            case ToolExecutionEndEvent te:
                state.CompleteTool(te.ToolCall, te.Result);
                break;
            case TurnEndEvent te:
                state.EndTurn(te.FinalMessage);
                break;
            case HarnessErrorEvent he:
                state.AddError(he.Message);
                break;
        }
    }
}
