using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Phi.Agent;

/// <summary>
/// The agent loop: drives the multi-turn model ↔ tool ↔ tool-result cycle
/// for one session, draining steering/follow-up queues at turn boundaries
/// so queued user prompts land as soon as the current direction finishes.
/// Mirrors tau's <c>run_agent_loop()</c> in <c>tau_agent.loop</c> — including
/// the steering-first / follow-up-second injection order, the
/// tool-call-driven turn continuation, and the <c>max_turns</c> safety cap.
/// <para>
/// Per-event mapping vs tau's <c>_assistant_events</c>: providers yield raw
/// granular events (<see cref="TextDeltaEvent"/>,
/// <see cref="ThinkingDeltaEvent"/>, <see cref="ToolCallEvent"/>, etc.) plus
/// terminal envelopes (<see cref="AssistantDoneEvent"/>,
/// <see cref="AssistantErrorEvent"/>). The loop accumulates the running
/// <see cref="AssistantMessage"/> partial by folding each granular event
/// through <see cref="AssistantMessageBuilder.Apply"/> (mirroring tau's
/// <c>canonicalize_provider_stream</c>), then yields a
/// <see cref="MessageUpdateEvent"/> wrapping the partial plus the original
/// raw event. Consumers (the projector) dispatch on the raw event type to
/// decide what to render.
/// </para>
/// <para>
/// <see cref="AssistantStartEvent"/> from the provider is a no-op here —
/// the loop already emitted <c>MessageStartEvent</c> before driving the
/// stream — but it's part of the protocol so providers / extensions can
/// observe an explicit begin signal if they want to.
/// </para>
/// <para>
/// At terminal the loop calls
/// <see cref="AssistantMessageBuilder.AdoptFinal"/> on the provider's
/// authoritative final <see cref="AssistantMessage"/> to fold in
/// <c>StopReason</c> / <c>Usage</c> / <c>Model</c> / <c>Api</c> /
/// <c>Provider</c>. <c>Content</c> stays as the streamed-order partial —
/// the provider's terminal build would reorder blocks (Anthropic prepends
/// thinking) and would clobber the projector state. Mirrors tau's
/// <c>final.content = [block.model_copy(...) for block in partial.content]</c>.
/// </para>
/// </summary>
public static class AgentLoop
{
    /// <summary>
    /// Runs the agent loop until the model emits a message with no tool
    /// calls, <paramref name="maxTurns"/> is exceeded, or the cancellation
    /// token fires. <see cref="Harness"/> delegates here and only adds
    /// session-level concerns (initial prompt, cancel handling,
    /// interrupted-tool placeholders).
    /// <para>
    /// Yields <see cref="AgentStartEvent"/> + a sequence of
    /// <see cref="TurnStartEvent"/> / <see cref="MessageStartEvent"/> /
    /// <see cref="MessageUpdateEvent"/>* / <see cref="MessageEndEvent"/> /
    /// <see cref="ToolExecutionStartEvent"/> /
    /// <see cref="ToolExecutionEndEvent"/>* / <see cref="TurnEndEvent"/>,
    /// followed by <see cref="AgentEndEvent"/> on completion.
    /// </para>
    /// </summary>
    public static async IAsyncEnumerable<HarnessEvent> RunAgentAsync(
        IPhiProvider provider,
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        Func<IReadOnlyList<IAgentMessage>>? getSteeringMessages = null,
        Func<IReadOnlyList<IAgentMessage>>? getFollowUpMessages = null,
        int? maxTurns = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var toolByName = tools.ToDictionary(t => t.Name);
        var turn = 0;
        var newMessages = new List<IAgentMessage>();

        yield return new AgentStartEvent();

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
                    foreach (var m in steering)
                    {
                        messages.Add(m);
                        newMessages.Add(m);
                        yield return new MessageStartEvent(m);
                        yield return new MessageEndEvent(m);
                    }
                    continue;
                }
            }

            turn++;

            if (maxTurns is not null && turn > maxTurns)
            {
                // Mirrors tau's _error_message: model + stop_reason +
                // message only; Api/Provider stay "unknown" (no provider
                // produced this message).
                var overrun = new AssistantMessage
                {
                    Model = model,
                    Content = [new TextBlock($"Agent stopped after max_turns={maxTurns}")],
                    StopReason = StopReasons.Error,
                };
                messages.Add(overrun);
                newMessages.Add(overrun);
                yield return new MessageStartEvent(overrun);
                yield return new MessageEndEvent(overrun);
                yield return new TurnEndEvent(overrun);
                yield return new AgentEndEvent(newMessages);
                yield break;
            }

            yield return new TurnStartEvent(turn);

            // The partial is the canonical running state of the assistant
            // message during this turn. The provider yields granular events;
            // we accumulate them via AssistantMessageBuilder.Apply — the same
            // canonicalizer tau's canonicalize_provider_stream runs in its
            // own layer.
            //
            // Identity metadata (Api / Provider) stays at the "unknown"
            // default while streaming: the loop doesn't know the provider's
            // identity (tau passes it into the canonicalizer from the
            // provider layer). AdoptFinal adopts the real values from the
            // provider's terminal message, which carries them from config.
            var partial = new AssistantMessage { Model = model };
            yield return new MessageStartEvent(partial);

            AssistantErrorEvent? lastError = null;
            bool terminal = false;

            await foreach (var ev in provider.StreamResponseAsync(
                model, system, messages, tools, cancellationToken))
            {
                switch (ev)
                {
                    case AssistantStartEvent:
                        // No-op: loop already yielded MessageStartEvent(partial)
                        // before driving the stream. Kept in the switch so we
                        // don't emit a spurious MessageUpdateEvent with an
                        // unchanged partial for the begin marker.
                        break;
                    case AssistantDoneEvent end:
                        partial = AssistantMessageBuilder.AdoptFinal(partial, end.Message);
                        terminal = true;
                        break;
                    case AssistantErrorEvent err:
                        lastError = err;
                        break;
                    default:
                        // Every granular event folds into the running partial
                        // via the canonicalizer. Yield a MessageUpdateEvent
                        // carrying both the partial and the original raw
                        // event so consumers (the projector) can dispatch on
                        // the raw event type without the loop caring about
                        // the distinction.
                        partial = AssistantMessageBuilder.Apply(partial, ev);
                        yield return new MessageUpdateEvent(partial, ev);
                        break;
                }
            }

            if (!terminal)
            {
                // The provider stream ended without a terminal response.
                // Mirror tau's canonicalize_provider_stream: turn the failure
                // into a terminal assistant message with StopReason=Error
                // (persisted for diagnostics, excluded from future provider
                // context by the providers' message conversion) instead of
                // throwing.
                var detail = lastError is not null
                    ? $" Last provider error: {lastError.Message}"
                    : " Stream ended without a final response.";
                partial = partial with
                {
                    StopReason = StopReasons.Error,
                    ErrorMessage = $"Provider produced no AssistantDoneEvent.{detail}",
                };
            }

            yield return new MessageEndEvent(partial);
            messages.Add(partial);
            newMessages.Add(partial);

            // If the provider signals an error or the request was aborted,
            // stop immediately — don't attempt tool execution.
            if (partial.StopReason is StopReasons.Error or StopReasons.Aborted)
            {
                yield return new TurnEndEvent(partial);
                yield return new AgentEndEvent(newMessages);
                yield break;
            }

            if (partial.ToolCalls.Count == 0)
            {
                yield return new TurnEndEvent(partial);

                // Turn ended naturally — drain the queues one last time.
                if (getFollowUpMessages is { } getFollowUp)
                {
                    var followUp = getFollowUp();
                    if (followUp.Count > 0)
                    {
                        foreach (var m in followUp)
                        {
                            messages.Add(m);
                            newMessages.Add(m);
                            yield return new MessageStartEvent(m);
                            yield return new MessageEndEvent(m);
                        }
                        continue;
                    }
                }

                yield return new AgentEndEvent(newMessages);
                yield break;
            }

            var toolResults = new List<ToolResultMessage>(partial.ToolCalls.Count);
            foreach (var call in partial.ToolCalls)
            {
                yield return new ToolExecutionStartEvent(call.Id, call.Name, call.Arguments);

                var result = await ExecuteToolSafelyAsync(
                    toolByName, call.Name, call.Id, call.Arguments, cancellationToken);

                yield return new ToolExecutionEndEvent(
                    call.Id, call.Name, result, IsError: result.IsError);

                var toolResultMessage = new ToolResultMessage
                {
                    ToolCallId = call.Id,
                    ToolName = call.Name,
                    Content = result.Content,
                    Details = result.Details,
                    IsError = result.IsError,
                };
                messages.Add(toolResultMessage);
                newMessages.Add(toolResultMessage);
                toolResults.Add(toolResultMessage);

                yield return new MessageStartEvent(toolResultMessage);
                yield return new MessageEndEvent(toolResultMessage);
            }

            yield return new TurnEndEvent(partial, toolResults);
        }
    }

    private static async Task<ToolResult> ExecuteToolSafelyAsync(
        Dictionary<string, Tool> toolByName,
        string name,
        string id,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!toolByName.TryGetValue(name, out var tool))
            {
                return new ToolResult(
                    [new TextBlock($"Unknown tool: {name}")],
                    IsError: true);
            }
            return await tool.ExecuteAsync(name, id, arguments, cancellationToken);
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
