namespace PhiCoding.Providers;

/// <summary>
/// One built-in provider: a vendor endpoint, the wire format it speaks, the
/// models it offers, and which credential-name slot its API key lives under
/// in <see cref="ICredentialStore"/>. Static data — see
/// <see cref="ProviderCatalog"/>.
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

    /// <summary>Credential-name slot in the <see cref="ICredentialStore"/>
    /// where the API key is persisted. Users add a key through the in-app
    /// <c>/connect</c> flow (TUI) or Providers page (desktop); the app then
    /// hands it to the provider without reading any environment variable.
    /// </summary>
    public required string CredentialName { get; init; }

    /// <summary>Models this provider offers; the picker in <c>/models</c>.</summary>
    public required IReadOnlyList<string> Models { get; init; }

    /// <summary>Model used when connecting without an explicit choice.</summary>
    public required string DefaultModel { get; init; }
}
