using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;

namespace PhiAgent;

/// <summary>
/// The agent harness: owns session state and runs the outer loop with
/// steering and follow-up injection points. Delegates inner-loop work to
/// <see cref="Loop"/>. Accepts tools as <see cref="IHarnessTool"/> so
/// application code registers typed tools directly without manual dispatch.
/// </summary>
public sealed class Harness
{
    private readonly IPhiProvider _provider;
    private readonly IReadOnlyList<Tool> _slimTools;
    private readonly Dictionary<string, IHarnessTool> _toolMap;
    private readonly string _model;
    private readonly string _system;
    private readonly List<IAgentMessage> _messages = new();

    public Harness(
        IPhiProvider provider,
        IReadOnlyList<IHarnessTool> tools,
        string model,
        string system = "")
    {
        _provider = provider;
        _slimTools = tools.Select(t => t.Tool).ToList();
        _toolMap = tools.ToDictionary(t => t.Tool.Name);
        _model = model;
        _system = system;
    }

    /// <summary>All messages accumulated across this session (user, assistant, tool results).</summary>
    public IReadOnlyList<IAgentMessage> Messages => _messages;

    /// <summary>
    /// Runs a full session: outer loop over turns. After each turn, the
    /// optional <paramref name="getSteeringMessages"/> and
    /// <paramref name="getFollowUpMessages"/> callbacks are queried for
    /// new messages to inject; if either returns a non-empty list, the loop
    /// continues with those messages pending — matching tau's AgentHarness._run
    /// continue pattern.
    /// </summary>
    public async IAsyncEnumerable<HarnessEvent> RunAsync(
        string initialPrompt,
        Func<IReadOnlyList<IAgentMessage>>? getSteeringMessages = null,
        Func<IReadOnlyList<IAgentMessage>>? getFollowUpMessages = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pending = new List<IAgentMessage>
        {
            new UserMessage { Content = initialPrompt }
        };
        var turn = 0;

        while (true)
        {
            foreach (var msg in pending) _messages.Add(msg);
            pending.Clear();
            turn++;
            yield return new TurnStartEvent(turn);

            await foreach (var ev in Loop.RunTurnAsync(
                _provider, _model, _system, _messages, _slimTools,
                ExecuteToolByName, cancellationToken))
            {
                yield return ev;
            }

            if (getSteeringMessages is not null)
            {
                var steering = getSteeringMessages();
                if (steering.Count > 0)
                {
                    pending.AddRange(steering);
                    continue;
                }
            }

            if (getFollowUpMessages is not null)
            {
                var followUp = getFollowUpMessages();
                if (followUp.Count > 0)
                {
                    pending.AddRange(followUp);
                    continue;
                }
            }

            yield break;
        }
    }

    private Task<ToolResult> ExecuteToolByName(string name, string id, JsonNode args, CancellationToken ct)
    {
        if (_toolMap.TryGetValue(name, out var tool))
            return tool.ExecuteAsync(name, id, args, ct);

        return Task.FromResult(new ToolResult(
            [new TextBlock($"Unknown tool: {name}")],
            IsError: true));
    }
}