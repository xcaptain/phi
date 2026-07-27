using PhiAgent;

namespace PhiProvider;

/// <summary>
/// Provider-neutral streaming interface for any OpenAI-compatible chat API.
/// Mirrors tau's <c>ModelProvider</c> protocol; <c>IAgentMessage</c> is the
/// marker that lets a heterogeneous list round-trip without a union type.
/// </summary>
public interface IPhiProvider
{
    IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
        string model,
        string system,
        IReadOnlyList<IAgentMessage> messages,
        IReadOnlyList<AgentTool> tools,
        CancellationToken cancellationToken = default);
}