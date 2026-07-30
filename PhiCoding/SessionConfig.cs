namespace PhiCoding;

/// <summary>
/// Configuration for creating a full <see cref="CodingSession"/> with
/// runtime. Passed to <see cref="CodingSession.Create(SessionConfig)"/>
/// which builds the provider, tools, and harness internally.
/// </summary>
public sealed record SessionConfig
{
    /// <summary>Working directory for this session.</summary>
    public string Cwd { get; init; } = Environment.CurrentDirectory;

    /// <summary>Provider type: <c>"anthropic"</c> or <c>"openai"</c>.</summary>
    public string ProviderType { get; init; } = "openai";

    /// <summary>API key for the LLM provider.</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>Base URL for the LLM provider API.</summary>
    public string BaseUrl { get; init; } = "";

    /// <summary>Provider display name (recorded on messages).</summary>
    public string? ProviderName { get; init; }

    /// <summary>Model name (e.g. <c>"deepseek-v4-flash"</c>).</summary>
    public string Model { get; init; } = "";

    /// <summary>System prompt for the agent.</summary>
    public string SystemPrompt { get; init; } = "";

    /// <summary>Max turns before the agent stops.</summary>
    public int MaxTurns { get; init; } = 50;
}
