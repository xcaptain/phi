using System.Runtime.CompilerServices;
using Phi.Agent;

namespace Phi.Avalonia.Tests.Helpers;

/// <summary>
/// In-memory <see cref="IPhiProvider"/> for session-runtime tests. Each
/// call is handled by a user-supplied delegate keyed by call index.
/// </summary>
public sealed class StubProvider(Func<int, CancellationToken, IAsyncEnumerable<ProviderEvent>> handler) : IPhiProvider
{
    private int _callCount;

    public void Dispose() { }

    /// <summary>Every call yields the same events.</summary>
    public static StubProvider Echo(params ProviderEvent[] turnEvents) =>
        new((_, ct) => Emit(turnEvents, ct));

    public async IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _callCount) - 1;
        await foreach (var ev in handler(call, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            yield return ev;
        }
    }

    private static async IAsyncEnumerable<ProviderEvent> Emit(
        IEnumerable<ProviderEvent> events,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var ev in events)
        {
            ct.ThrowIfCancellationRequested();
            yield return ev;
            await Task.Yield();
        }
    }
}
