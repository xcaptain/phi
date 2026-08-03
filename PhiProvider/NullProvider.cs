using System.Runtime.CompilerServices;
using PhiAgent;

namespace PhiProvider;

/// <summary>
/// Placeholder provider used when the app starts without any configured
/// credentials (no env var, no stored key). Every request surfaces a
/// <see cref="ProviderErrorEvent"/> pointing the user at <c>/connect</c>;
/// it holds no resources and <see cref="Dispose"/> is a no-op. Replaced by a
/// real provider the moment the user connects one.
/// </summary>
public sealed class NullProvider : IPhiProvider
{
    public async IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ProviderErrorEvent(
            "No provider connected. Run /connect to connect a provider.");
        await Task.Yield();
    }

    public void Dispose() { }
}
