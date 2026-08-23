using System.Runtime.CompilerServices;

namespace Phi.Agent;

/// <summary>
/// The agent harness: thin wrapper around <see cref="AgentLoop.RunAgentAsync"/>
/// that owns session state (<see cref="Messages"/>) and adds the two
/// session-level concerns the loop doesn't handle:
/// <list type="bullet">
/// <item>Initial prompt injection.</item>
/// <item>Converting an in-flight cancellation into a
/// <see cref="HarnessErrorEvent"/> + synthetic
/// <c>Tool call interrupted by user</c> placeholders via
/// <see cref="AppendInterruptedToolResults"/>, so the next session sees a
/// well-formed history.</item>
/// </list>
/// Tool registration, multi-turn orchestration, and steering/follow-up
/// injection all live in <see cref="AgentLoop"/>, matching tau's split between
/// <c>tau_agent.harness</c> (thin wrapper) and <c>tau_agent.loop</c>
/// (run_agent_loop).
/// </summary>
public sealed class Harness(
    IPhiProvider provider,
    IReadOnlyList<Tool> tools,
    string model,
    string system = "",
    int? maxTurns = null)
{
    // Mutable so the extension runtime can add tools after construction
    // (extensions are loaded after the session is composed, so tools have
    // to register post-ApplyRuntime — see Sprint 1 design in
    // docs/extensions.md §14).
    private readonly List<Tool> _tools = [.. tools];
    private readonly string _system = system;
    private readonly int? _maxTurns = maxTurns;
    private readonly List<IAgentMessage> _messages = [];

    /// <summary>Read-only view of the tool set (built-in + extension tools).</summary>
    public IReadOnlyList<Tool> Tools => _tools;

    /// <summary>
    /// Append a tool after construction. Used by the extension runtime to
    /// register tools post-ApplyRuntime. Replaces the old design where
    /// Session had to rebuild the harness on every extension registration.
    /// </summary>
    public void AddTool(Tool tool) => _tools.Add(tool);

    /// <summary>
    /// Removes every tool matching <paramref name="predicate"/>. Used by the
    /// extension reload path to drop old-extension tools before the new set
    /// is registered — otherwise the harness would keep strong references to
    /// the unloaded extension's assembly (which defeats the collectible-ALC
    /// GC unload). Returns the number of tools removed.
    /// </summary>
    public int RemoveTools(Predicate<Tool> predicate) =>
        _tools.RemoveAll(predicate);

    /// <summary>
    /// Provider used for the next <see cref="RunAsync"/> call. Mutable so a
    /// session can switch providers between runs without rebuilding the
    /// harness (the in-flight run keeps the values it started with).
    /// </summary>
    public IPhiProvider Provider { get; set; } = provider;

    /// <summary>
    /// Model used for the next <see cref="RunAsync"/> call. Mutable so a
    /// session can switch models between runs; <c>AgentLoop</c> treats the
    /// model as a per-request parameter.
    /// </summary>
    public string Model { get; set; } = model;

    /// <summary>All messages accumulated across this session (user, assistant, tool results).</summary>
    public IReadOnlyList<IAgentMessage> Messages => _messages;

    /// <summary>
    /// Appends a message to the session history. Used by session resume
    /// (loading from disk) and tests. <see cref="RunAsync"/> mutates the
    /// same list internally.
    /// </summary>
    public void AppendMessage(IAgentMessage message) => _messages.Add(message);

    /// <summary>
    /// Replaces the entire message history. Used when loading a persisted
    /// session from disk (resume). Callers should supply messages in
    /// conversation order.
    /// </summary>
    public void ReplaceMessages(IReadOnlyList<IAgentMessage> messages)
    {
        _messages.Clear();
        _messages.AddRange(messages);
    }

    /// <summary>
    /// Scans session history for assistant tool calls that have no matching
    /// <see cref="ToolResultMessage"/>, and appends a synthetic
    /// "Tool call interrupted by user" result for each. Returns the number of
    /// placeholders inserted. Idempotent — calling twice is a no-op the
    /// second time.
    /// <para>
    /// Mirrors tau's <c>AgentHarness._append_interrupted_tool_results</c>:
    /// lets the conversation stay well-formed after the user cancels mid-turn
    /// so the next steering/follow-up message lands on a coherent history.
    /// </para>
    /// </summary>
    public int AppendInterruptedToolResults()
    {
        var returnedIds = new HashSet<string>(
            _messages.OfType<ToolResultMessage>().Select(m => m.ToolCallId));

        // Snapshot messages before iterating — we may append to _messages
        // inside the loop, which would invalidate the live enumerator.
        var snapshot = _messages.ToList();

        var inserted = 0;
        foreach (var msg in snapshot)
        {
            if (msg is not AssistantMessage assistant) continue;
            foreach (var call in assistant.ToolCalls)
            {
                if (!returnedIds.Add(call.Id)) continue;
                _messages.Add(new ToolResultMessage
                {
                    ToolCallId = call.Id,
                    ToolName = call.Name,
                    Content = [new TextBlock("Tool call interrupted by user")],
                    IsError = true,
                });
                inserted++;
            }
        }
        return inserted;
    }

    /// <summary>
    /// Runs a full session by delegating to <see cref="AgentLoop.RunAgentAsync"/>.
    /// The loop drives multi-turn execution and drains the steering/follow-up
    /// queues; this method handles two session-level concerns only:
    /// <list type="bullet">
    /// <item>Seed the initial user prompt into <see cref="Messages"/>.</item>
    /// <item>If <paramref name="cancellationToken"/> fires mid-turn, catch
    /// the resulting <see cref="OperationCanceledException"/> (which the
    /// loop propagates), append interrupted tool placeholders via
    /// <see cref="AppendInterruptedToolResults"/>, and surface a
    /// <see cref="HarnessErrorEvent"/>. The session ends normally
    /// (yield break) — callers wanting to continue should inspect
    /// <see cref="Messages"/> and start a new <see cref="RunAsync"/>
    /// invocation.</item>
    /// </list>
    /// </summary>
    public async IAsyncEnumerable<HarnessEvent> RunAsync(
        string initialPrompt,
        Func<IReadOnlyList<IAgentMessage>>? getSteeringMessages = null,
        Func<IReadOnlyList<IAgentMessage>>? getFollowUpMessages = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _messages.Add(new UserMessage { Content = initialPrompt });

        var cancelled = false;
        // Manual enumerator so we can catch OperationCanceledException around
        // MoveNextAsync without violating CS1626 (yield in try-catch) while
        // preserving streaming semantics.
        var enumerator = AgentLoop.RunAgentAsync(
                Provider, Model, _system, _messages, _tools,
                getSteeringMessages, getFollowUpMessages,
                _maxTurns, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }

                if (!hasNext) break;
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (cancelled)
        {
            var inserted = AppendInterruptedToolResults();
            yield return new HarnessErrorEvent(
                inserted > 0
                    ? $"interrupted ({inserted} tool call(s) cancelled)"
                    : "interrupted");
        }
    }
}
