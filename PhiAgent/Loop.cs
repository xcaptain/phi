using System.Runtime.CompilerServices;

namespace PhiAgent;

/// <summary>
/// Inner loop logic: one turn's worth of model ↔ tool interactions.
/// Pure function — no session state, no outer-loop concerns. Appends the
/// assistant turn and any tool results to a fresh copy of the input
/// messages list (the caller observes the appended messages via
/// <c>Harness.Messages</c>). Mirrors tau's <c>run_agent_loop()</c>.
/// </summary>
public static class Loop
{
    /// <summary>
    /// Runs the inner loop until the model produces a final message with no
    /// tool calls. Yields <see cref="AssistantTextDeltaEvent"/>,
    /// <see cref="AssistantToolCallEvent"/>, <see cref="ToolExecutionStartEvent"/>,
    /// <see cref="ToolExecutionEndEvent"/>, and one terminating
    /// <see cref="TurnEndEvent"/>.
    /// </summary>
    public static async IAsyncEnumerable<HarnessEvent> RunTurnAsync(
        IPhiProvider provider,
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        ToolExecutor executeTool,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AssistantMessage? final = null;
        ProviderErrorEvent? lastError = null;

        while (true)
        {
            final = null;
            lastError = null;

            await foreach (var ev in provider.StreamResponseAsync(
                model, system, messages, tools, cancellationToken))
            {
                switch (ev)
                {
                    case ProviderTextDeltaEvent t:
                        yield return new AssistantTextDeltaEvent(t.Delta);
                        break;
                    case ProviderToolCallEvent tc:
                        yield return new AssistantToolCallEvent(tc.ToolCall);
                        break;
                    case ProviderResponseEndEvent end:
                        final = end.Message;
                        break;
                    case ProviderErrorEvent err:
                        lastError = err;
                        break;
                }
            }

            if (final is null)
            {
                var detail = lastError is not null
                    ? $" Last provider error: {lastError.Message}"
                    : " Stream ended without a final response.";
                throw new InvalidOperationException(
                    $"Provider produced no ProviderResponseEndEvent.{detail}");
            }

            messages.Add(final);

            if (final.ToolCalls.Count == 0)
            {
                yield return new TurnEndEvent(final);
                yield break;
            }

            foreach (var call in final.ToolCalls)
            {
                yield return new ToolExecutionStartEvent(call.Id, call.Name);

                var result = await executeTool(call.Name, call.Id, call.Arguments, cancellationToken);

                var toolResultMessage = new ToolResultMessage
                {
                    ToolCallId = call.Id,
                    ToolName = call.Name,
                    Content = result.Content,
                    IsError = result.IsError,
                };
                messages.Add(toolResultMessage);

                yield return new ToolExecutionEndEvent(call, result);
            }
        }
    }
}