namespace PhiProvider;

/// <summary>
/// Configuration for an Anthropic Messages API endpoint. Covers the official
/// Anthropic API plus providers that re-publish the same wire format.
/// Reasoning knobs land here later as optional fields.
/// </summary>
public sealed record AnthropicConfig
{
    public string ApiKey { get; init; } = "";

    public string BaseUrl { get; init; } = "https://api.anthropic.com";

    /// <summary>Wire API identifier recorded on the final <c>AssistantMessage.Api</c>.</summary>
    public string Api { get; init; } = "anthropic-messages";

    /// <summary>Provider display name recorded on the final <c>AssistantMessage.Provider</c>.</summary>
    public string Provider { get; init; } = "anthropic";

    /// <summary>
    /// Anthropic API version header. Bump only when the SDK needs to opt into
    /// newer server-side behavior; the wire format is otherwise stable.
    /// </summary>
    public string AnthropicVersion { get; init; } = "2023-06-01";

    /// <summary>
    /// Required by the Anthropic API. The model won't respond without it.
    /// Bumped automatically when <c>ThinkingBudgetTokens</c> is set.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Switch the auth header from <c>x-api-key</c> to <c>Authorization: Bearer</c>.
    /// Use for OAuth-style or proxied providers that expect Bearer tokens.
    /// </summary>
    public bool BearerAuth { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);


}