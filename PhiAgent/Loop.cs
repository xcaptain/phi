using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace PhiAgent;

/// <summary>
/// The agent loop: drives the multi-turn model ↔ tool ↔ tool-result cycle
/// for one session, draining steering/follow-up queues at turn boundaries
/// so queued user prompts land as soon as the current direction finishes.
/// Mirrors tau's <c>run_agent_loop()</c> in <c>tau_agent.loop</c> — including
/// the steering-first / follow-up-second injection order, the
/// tool-call-driven turn continuation, and the <c>max_turns</c> safety cap.
/// <para>
/// <b>Note on naming</b>: this method is named <see cref="RunAgentAsync"/>
/// (not <c>RunTurnAsync</c>) because the loop drives <i>multiple</i> turns
/// per call — each iteration is one model round-trip plus any tool
/// executions. The loop terminates when the model emits a message with no
/// tool calls, when <paramref name="maxTurns"/> is exceeded, or when
/// <paramref name="cancellationToken"/> fires. <see cref="Harness"/>
/// delegates here and only adds session-level concerns (initial prompt,
/// cancel handling, interrupted-tool placeholders).
/// </para>
/// </summary>
public static class Loop
{
    /// <summary>
    /// Runs the agent loop until the model stops emitting tool calls,
    /// <paramref name="maxTurns"/> is exceeded, or the cancellation token
    /// fires. Yields <see cref="AssistantTextDeltaEvent"/>,
    /// <see cref="AssistantThinkingStartEvent"/> /
    /// <see cref="AssistantThinkingDeltaEvent"/> /
    /// <see cref="AssistantThinkingEndEvent"/>,
    /// <see cref="AssistantToolCallEvent"/>,
    /// <see cref="ToolExecutionStartEvent"/>,
    /// <see cref="ToolExecutionEndEvent"/>,
    /// <see cref="HarnessErrorEvent"/> (on <c>max_turns</c>),
    /// and one terminating <see cref="TurnEndEvent"/> per successful turn.
    /// </summary>
    /// <param name="getSteeringMessages">
    /// Called at the <b>start of every iteration</b> to drain queued
    /// "redirect" messages. If it returns a non-empty list, the messages
    /// are appended to <paramref name="messages"/> and the next turn starts
    /// immediately without yielding a <see cref="TurnStartEvent"/> for the
    /// empty iteration. Steering does <i>not</i> consume a turn slot.
    /// </param>
    /// <param name="getFollowUpMessages">
    /// Called when a turn ends naturally (model produced no tool calls) to
    /// drain queued "additional task" messages. Non-empty results append
    /// to <paramref name="messages"/> and start a new turn.
    /// </param>
    /// <param name="maxTurns">
    /// Optional cap on the number of provider rounds inside one call.
    /// Steering iterations don't count toward this cap; only actual turns
    /// do. When exceeded, the loop synthesizes an error assistant message
    /// and stops.
    /// </param>
    public static async IAsyncEnumerable<HarnessEvent> RunAgentAsync(
        IPhiProvider provider,
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        ToolExecutor executeTool,
        Func<IReadOnlyList<IAgentMessage>>? getSteeringMessages = null,
        Func<IReadOnlyList<IAgentMessage>>? getFollowUpMessages = null,
        int? maxTurns = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var turn = 0;
        while (true)
        {
            // Steering is checked first, before incrementing the turn counter
            // and before yielding TurnStartEvent — a pure redirect doesn't
            // "start" a new turn from the caller's perspective.
            if (getSteeringMessages is { } getSteering)
            {
                var steering = getSteering();
                if (steering.Count > 0)
                {
                    foreach (var m in steering) messages.Add(m);
                    continue;
                }
            }

            turn++;

            if (maxTurns is not null && turn > maxTurns)
            {
                var overrun = new AssistantMessage
                {
                    Api = provider.GetType().Name,
                    Provider = "agent",
                    Model = model,
                    Content = [new TextBlock($"Agent stopped after max_turns={maxTurns}")],
                    StopReason = StopReasons.Error,
                };
                messages.Add(overrun);
                yield return new HarnessErrorEvent(overrun.Text);
                yield return new TurnEndEvent(overrun);
                yield break;
            }

            yield return new TurnStartEvent(turn);

            AssistantMessage? final = null;
            ProviderErrorEvent? lastError = null;
            Stopwatch? thinkingStopwatch = null;
            double? thinkingDurationMs = null;

            await foreach (var ev in provider.StreamResponseAsync(
                model, system, messages, tools, cancellationToken))
            {
                switch (ev)
                {
                    case ProviderTextDeltaEvent t:
                        yield return new AssistantTextDeltaEvent(t.Delta);
                        break;
                    case ProviderThinkingStartEvent:
                        thinkingStopwatch = Stopwatch.StartNew();
                        thinkingDurationMs = null;
                        yield return new AssistantThinkingStartEvent();
                        break;
                    case ProviderThinkingDeltaEvent t:
                        yield return new AssistantThinkingDeltaEvent(t.Delta);
                        break;
                    case ProviderThinkingEndEvent end:
                        thinkingDurationMs = thinkingStopwatch?.ElapsedMilliseconds;
                        thinkingStopwatch = null;
                        var timedBlock = thinkingDurationMs is not null
                            ? end.Block with { DurationMs = thinkingDurationMs }
                            : end.Block;
                        yield return new AssistantThinkingEndEvent(timedBlock);
                        break;
                    case ProviderToolCallEvent tc:
                        yield return new AssistantToolCallEvent(tc.ToolCall);
                        break;
                    case ProviderResponseEndEvent end:
                        // Replace thinking blocks in the final message with
                        // timed variants so duration survives persistence.
                        final = end.Message with
                        {
                            Content = end.Message.Content
                                .Select(c => c is ThinkingBlock tb && thinkingDurationMs is { } d
                                    ? tb with { DurationMs = d }
                                    : c)
                                .ToList(),
                        };
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

            // If the provider signals an error or the request was aborted,
            // stop immediately — don't attempt tool execution.
            if (final.StopReason is StopReasons.Error or StopReasons.Aborted)
            {
                yield return new TurnEndEvent(final);
                yield break;
            }

            if (final.ToolCalls.Count == 0)
            {
                yield return new TurnEndEvent(final);

                // Turn ended naturally — drain the queues one last time.
                // Follow-up is checked first (it's the natural "more work"
                // channel after a turn ends). If empty, steering gets one
                // final check too, so a message enqueued after the turn
                // ended is still picked up before we give up.
                if (getFollowUpMessages is { } getFollowUp)
                {
                    var followUp = getFollowUp();
                    if (followUp.Count > 0)
                    {
                        foreach (var m in followUp) messages.Add(m);
                        continue;
                    }
                }

                if (getSteeringMessages is { } getSteeringFinal)
                {
                    var steeringFinal = getSteeringFinal();
                    if (steeringFinal.Count > 0)
                    {
                        foreach (var m in steeringFinal) messages.Add(m);
                        continue;
                    }
                }

                yield break;
            }

            foreach (var call in final.ToolCalls)
            {
                yield return new ToolExecutionStartEvent(call.Id, call.Name);

                var result = await ExecuteToolSafelyAsync(
                    executeTool, call.Name, call.Id, call.Arguments, cancellationToken);

                var toolResultMessage = new ToolResultMessage
                {
                    ToolCallId = call.Id,
                    ToolName = call.Name,
                    Content = result.Content,
                    Details = result.Details,
                    IsError = result.IsError,
                };
                messages.Add(toolResultMessage);

                yield return new ToolExecutionEndEvent(call, result);
            }
        }
    }

    private static async Task<ToolResult> ExecuteToolSafelyAsync(
        ToolExecutor executeTool,
        string name,
        string id,
        JsonNode arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await executeTool(name, id, arguments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolResult(
                [new TextBlock($"Tool '{name}' failed: {ex.Message}")],
                IsError: true);
        }
    }
}
