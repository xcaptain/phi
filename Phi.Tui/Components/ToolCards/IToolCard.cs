using Phi.Agent;
using XenoAtom.Terminal.UI;

namespace Phi.Tui.Components.ToolCards;

/// <summary>
/// Per-tool visual component for a single tool call in the chat transcript.
/// <see cref="ShowPending"/> renders the in-flight placeholder; <see cref="Complete"/>
/// swaps in the final title + body once the tool result arrives.
/// </summary>
public interface IToolCard
{
    Visual Visual { get; }
    void ShowPending(ToolCall toolCall);
    void Complete(ToolResult result);
}
