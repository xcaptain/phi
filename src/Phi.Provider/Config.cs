namespace Phi.Provider;

/// <summary>
/// Configuration for an OpenAI-compatible chat completions endpoint.
/// Only the fields a basic call needs are exposed; reasoning / thinking /
/// compat quirks land here later as optional knobs.
/// </summary>
public sealed record OpenAICompatibleConfig
{
    public string ApiKey { get; init; } = "";

    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    /// <summary>Wire API identifier recorded on the final <c>AssistantMessage.Api</c>.</summary>
    public string Api { get; init; } = "openai-completions";

    /// <summary>Provider display name recorded on the final <c>AssistantMessage.Provider</c>.</summary>
    public string Provider { get; init; } = "openai-compatible";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}
