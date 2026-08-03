namespace PhiCoding.Providers;

/// <summary>
/// One built-in provider: a vendor endpoint, the wire format it speaks, the
/// models it offers, and which env var / credential name its API key lives
/// under. Static data — see <see cref="ProviderCatalog"/>.
/// </summary>
public sealed record ProviderCatalogEntry
{
    /// <summary>Stable identifier, e.g. <c>"deepseek"</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Human-friendly name shown in the <c>/connect</c> picker.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Wire format; selects the provider implementation.</summary>
    public required ProviderKind Kind { get; init; }

    /// <summary>Base endpoint sent to the provider.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Environment variable that holds the API key (e.g. <c>DEEPSEEK_API_KEY</c>).</summary>
    public required string ApiKeyEnv { get; init; }

    /// <summary>Credential name used in the credential store.</summary>
    public required string CredentialName { get; init; }

    /// <summary>Models this provider offers; the picker in <c>/models</c>.</summary>
    public required IReadOnlyList<string> Models { get; init; }

    /// <summary>Model used when connecting without an explicit choice.</summary>
    public required string DefaultModel { get; init; }
}
