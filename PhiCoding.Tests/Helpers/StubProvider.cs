using System.Runtime.CompilerServices;
using PhiAgent;

namespace PhiCoding.Tests.Helpers;

/// <summary>
/// In-memory <see cref="IPhiProvider"/> for session-runtime tests. Each
/// call is handled by a user-supplied delegate keyed by call index, so a
/// test can make the first call block (simulating a slow model) while
/// later calls (e.g. the session auto-namer) respond instantly.
/// </summary>
public sealed class StubProvider(Func<int, CancellationToken, IAsyncEnumerable<ProviderEvent>> handler) : IPhiProvider
{
    private int _callCount;

    public int CallCount => _callCount;

    public void Dispose() { }

    /// <summary>Every call yields the same events.</summary>
    public static StubProvider Echo(params ProviderEvent[] turnEvents) =>
        new((_, ct) => Emit(turnEvents, ct));

    /// <summary>A complete single-response text turn.</summary>
    public static ProviderEvent[] TextTurn(string text) =>
    [
        new ProviderTextDeltaEvent(text),
        new ProviderResponseEndEvent(new AssistantMessage
        {
            Content = [new TextBlock(text)],
            StopReason = StopReasons.Stop,
        }),
    ];

    /// <summary>First call blocks until <paramref name="gate"/> completes
    /// (or the token cancels); later calls answer with <paramref name="text"/>.</summary>
    public static StubProvider FirstCallBlocks(TaskCompletionSource gate, string text = "ok") =>
        new((call, ct) => call == 0 ? Block(gate, ct) : Emit(TextTurn(text), ct));

    /// <summary>The first two calls throw (covering the auto-name probe plus
    /// the first real run); later calls answer with <paramref name="text"/>.
    /// Used to test that a new run clears the previous <c>LastError</c>.</summary>
    public static StubProvider FirstTwoCallsThrow(string text = "ok") =>
        new((call, ct) => call < 2 ? Throw(ct) : Emit(TextTurn(text), ct));

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

    private static async IAsyncEnumerable<ProviderEvent> Block(
        TaskCompletionSource gate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await gate.Task.WaitAsync(ct);
        foreach (var ev in TextTurn("unblocked"))
        {
            yield return ev;
        }
    }

    private static async IAsyncEnumerable<ProviderEvent> Throw(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (ct.IsCancellationRequested) yield break;
        throw new InvalidOperationException("stub provider failure");
    }
}
