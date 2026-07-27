using System.Runtime.CompilerServices;
using PhiAgent;

namespace PhiAgent.Tests;

/// <summary>In-memory <see cref="IPhiProvider"/> that replays queued turn event sequences.</summary>
public sealed class FakePhiProvider : IPhiProvider
{
    private readonly Queue<List<ProviderEvent>> _turns = new();
    private readonly List<List<IAgentMessage>> _callsReceived = new();

    public IReadOnlyList<IReadOnlyList<IAgentMessage>> CallsReceived => _callsReceived;

    public FakePhiProvider(IEnumerable<IEnumerable<ProviderEvent>> turns)
    {
        foreach (var turn in turns)
            _turns.Enqueue(turn.ToList());
    }

    public async IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _callsReceived.Add(messages.ToList());

        if (!_turns.TryDequeue(out var turn))
            throw new InvalidOperationException(
                "FakePhiProvider ran out of queued turns — provide one list per expected Stream call");

        foreach (var ev in turn)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ev;
            await Task.Yield();
        }
    }
}