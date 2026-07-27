namespace PhiAgent;

/// <summary>
/// Provider-neutral streaming interface for any OpenAI-compatible chat API.
/// Lives in <c>PhiAgent</c> because the agent harness is the consumer;
/// concrete implementations live in <c>PhiProvider</c>.
/// </summary>
public interface IPhiProvider
{
    IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        CancellationToken cancellationToken = default);
}