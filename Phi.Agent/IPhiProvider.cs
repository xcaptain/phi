namespace Phi.Agent;

/// <summary>
/// Provider-neutral streaming interface for any OpenAI-compatible chat API.
/// Lives in <c>Phi.Agent</c> because the agent harness is the consumer;
/// concrete implementations live in <c>Phi.Provider</c>.
/// <para>
/// Providers are long-lived resources (they own their HTTP transport) and
/// implement <see cref="IDisposable"/> so a session can release the outgoing
/// provider when switching to another one. The <c>model</c> is a per-request
/// parameter — swapping models within one provider never touches provider
/// resources.
/// </para>
/// </summary>
public interface IPhiProvider : IDisposable
{
    IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        CancellationToken cancellationToken = default);
}
