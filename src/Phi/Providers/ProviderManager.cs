using Phi.Agent;

namespace Phi.Providers;

/// <summary>
/// Application-level facade over the credential store and settings. The UI
/// (<c>/connect</c>, <c>/models</c> on the TUI; the Providers page on
/// desktop) uses this to look up API keys, persist the default selection,
/// and rebuild runtime <see cref="Phi.Agent.IPhiProvider"/> instances from
/// a catalog name. Catalog enumeration lives on
/// <see cref="ProviderCatalog"/> — this class only carries what needs the
/// <see cref="ICredentialStore"/> or settings file.
/// <para>
/// Implements <see cref="IProviderResolver"/> so the session composition
/// path (<see cref="Session.LoadAsync"/>) can rebuild a live provider from
/// the name stored in a <see cref="SessionRecord"/> on resume, falling back
/// to a no-op provider when no API key is available — the UI surfaces a
/// <c>/connect</c> prompt to recover.
/// </para>
/// </summary>
public sealed class ProviderManager : IProviderResolver
{
    private readonly ICredentialStore _credentials;
    private readonly string _settingsPath;

    public ProviderManager(ICredentialStore? credentials = null, string? settingsPath = null)
    {
        _credentials = credentials ?? new FileCredentialStore(FileCredentialStore.DefaultPath);
        _settingsPath = settingsPath ?? PhiSettings.DefaultPath;
    }

    /// <summary>Resolves the API key for a provider from the credential store.</summary>
    public string? ResolveApiKey(ProviderCatalogEntry entry) =>
        _credentials.Get(entry.CredentialName);

    /// <summary>Whether a key is available in the credential store for the provider.</summary>
    public bool HasApiKey(ProviderCatalogEntry entry) =>
        _credentials.Has(entry.CredentialName);

    /// <summary>Returns the resolved key or throws with actionable guidance.</summary>
    public string GetApiKey(ProviderCatalogEntry entry) =>
        ResolveApiKey(entry)
        ?? throw new InvalidOperationException(
            $"No API key for {entry.Name}. Run /connect (TUI) or open the Providers page (desktop) to add one.");

    /// <summary>Persists a key for the provider to the credential store.</summary>
    public void SetApiKey(ProviderCatalogEntry entry, string key) =>
        _credentials.Set(entry.CredentialName, key);

    /// <summary>Persists the default provider + model for the next launch.</summary>
    public void SaveDefault(ProviderCatalogEntry entry, string model) =>
        PhiSettings.Save(_settingsPath, new PhiSettings
        {
            DefaultProvider = entry.Name,
            DefaultModel = model,
        });

    /// <summary>
    /// Default provider for a fresh launch: the persisted one when known,
    /// otherwise the first entry in the catalog.
    /// </summary>
    public ProviderCatalogEntry ResolveDefaultProvider()
    {
        var name = PhiSettings.Load(_settingsPath).DefaultProvider;
        return ProviderCatalog.Find(name) ?? ProviderCatalog.All[0];
    }

    /// <summary>
    /// Default model for a provider: the persisted model when it belongs to
    /// that provider, otherwise the provider's own default.
    /// </summary>
    public string ResolveDefaultModel(ProviderCatalogEntry entry)
    {
        var settings = PhiSettings.Load(_settingsPath);
        if (settings.DefaultProvider.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.DefaultModel)
            && entry.Models.Contains(settings.DefaultModel))
        {
            return settings.DefaultModel;
        }
        return entry.DefaultModel;
    }

    /// <summary>
    /// Resolves a runtime provider by catalog name: looks up the entry,
    /// picks up the API key from the credential store, and constructs the
    /// live instance. An empty <paramref name="providerName"/> falls
    /// through to <see cref="ResolveDefaultProvider"/>, so callers that
    /// have no preference (e.g. a session record written before the
    /// provider was ever recorded) still get a working instance. Falls
    /// back to <see cref="Phi.Provider.NullProvider"/> when no key is
    /// available so the TUI can open and prompt for <c>/connect</c>.
    /// Throws when the name is non-empty but not in the catalog.
    /// </summary>
    public IPhiProvider Resolve(string providerName)
    {
        var entry = string.IsNullOrEmpty(providerName)
            ? ResolveDefaultProvider()
            : ProviderCatalog.Find(name: providerName)
              ?? throw new ArgumentException($"Unknown provider: {providerName}", nameof(providerName));
        if (ResolveApiKey(entry) is { } apiKey)
            return CreateProvider(entry, apiKey);
        return new Phi.Provider.NullProvider();
    }

    /// <summary>
    /// Builds a runtime provider for the entry; the caller owns it. Static
    /// because the construction is a pure function of
    /// <paramref name="entry"/> + <paramref name="apiKey"/> — no instance
    /// state involved, so callers can construct a provider without holding
    /// a <see cref="ProviderManager"/> reference.
    /// </summary>
    public static IPhiProvider CreateProvider(ProviderCatalogEntry entry, string apiKey)
    {
        switch (entry.Kind)
        {
            case ProviderKind.OpenAICompatible:
                var openAiConfig = new Phi.Provider.OpenAICompatibleConfig
                {
                    ApiKey = apiKey,
                    BaseUrl = entry.BaseUrl,
                    Provider = entry.Name,
                };
                // HttpClient.Timeout covers the whole request including the
                // streamed body — wire the configured value explicitly so a
                // long stream isn't killed by the 100s default.
                return new Phi.Provider.OpenAICompatibleProvider(
                    openAiConfig, new HttpClient { Timeout = openAiConfig.Timeout });
            case ProviderKind.Anthropic:
                var anthropicConfig = new Phi.Provider.AnthropicConfig
                {
                    ApiKey = apiKey,
                    BaseUrl = entry.BaseUrl,
                    Provider = entry.Name,
                };
                return new Phi.Provider.AnthropicProvider(
                    anthropicConfig, new HttpClient { Timeout = anthropicConfig.Timeout });
            default:
                throw new NotSupportedException($"Provider kind {entry.Kind} is not implemented");
        }
    }
}
