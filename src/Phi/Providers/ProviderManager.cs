using System.Diagnostics.CodeAnalysis;
using Phi.Agent;
using Phi.Provider;

namespace Phi.Providers;

/// <summary>
/// Application-level facade over the provider catalog, credential store, and
/// settings. This is what <c>Program.cs</c> and the TUI (<c>/connect</c>,
/// <c>/models</c>) use to resolve providers, look up API keys, build runtime
/// <see cref="IPhiProvider"/> instances, and persist the default selection.
/// <para>
/// Providers are constructed here and handed to the session, which takes
/// ownership (disposing the outgoing provider on switch). API keys are
/// resolved from <see cref="ICredentialStore"/> — the file-backed
/// <see cref="FileCredentialStore"/> by default; tests can inject a fake.
/// Users add keys through the in-app <c>/connect</c> flow (TUI) or the
/// Providers page (desktop).
/// </para>
/// <para>
/// Also implements <see cref="IProviderResolver"/> so the session factory
/// can rebuild a live provider from a name stored in a
/// <see cref="SessionRecord"/> on resume.
/// </para>
/// </summary>
[SuppressMessage("Performance", "CA1822", Justification = "Service facade; instance members stay swappable/injectable")]
public sealed class ProviderManager(
    ICredentialStore? credentials = null,
    string? settingsPath = null) : IProviderResolver
{
    private readonly ICredentialStore _credentials = credentials ?? new FileCredentialStore(FileCredentialStore.DefaultPath);
    private readonly string _settingsPath = settingsPath ?? PhiSettings.DefaultPath;

    /// <summary>All connectable providers, in <c>/connect</c> display order.</summary>
    public IReadOnlyList<ProviderCatalogEntry> Providers => ProviderCatalog.All;

    /// <summary>Returns a catalog entry by name; throws when unknown.</summary>
    public ProviderCatalogEntry GetProvider(string name) =>
        ProviderCatalog.Find(name)
        ?? throw new ArgumentException($"Unknown provider: {name}", nameof(name));

    /// <summary>Models the given provider offers, for the <c>/models</c> picker.</summary>
    public IReadOnlyList<string> GetAvailableModels(ProviderCatalogEntry entry) => entry.Models;

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

    /// <summary>Builds a runtime provider for the entry; the caller owns it.</summary>
    public IPhiProvider CreateProvider(ProviderCatalogEntry entry, string apiKey) => entry.Kind switch
    {
        ProviderKind.OpenAICompatible => new OpenAICompatibleProvider(
            new OpenAICompatibleConfig
            {
                ApiKey = apiKey,
                BaseUrl = entry.BaseUrl,
                Provider = entry.Name,
            },
            new HttpClient()),
        ProviderKind.Anthropic => new AnthropicProvider(
            new AnthropicConfig
            {
                ApiKey = apiKey,
                BaseUrl = entry.BaseUrl,
                Provider = entry.Name,
            },
            new HttpClient()),
        _ => throw new NotSupportedException($"Provider kind {entry.Kind} is not implemented"),
    };

    /// <summary>
    /// Resolves a runtime provider by catalog name: looks up the entry,
    /// picks up the API key from env or the credential store, and
    /// constructs the live instance. An empty <paramref name="providerName"/>
    /// falls through to <see cref="ResolveDefaultProvider"/>, so callers
    /// that have no preference (e.g. a session record written before the
    /// provider was ever recorded) still get a working instance. Falls
    /// back to <see cref="NullProvider"/> when no key is available so the
    /// TUI can open and prompt for <c>/connect</c>. Throws when the name
    /// is non-empty but not in the catalog.
    /// </summary>
    public IPhiProvider Resolve(string providerName)
    {
        var entry = string.IsNullOrEmpty(providerName)
            ? ResolveDefaultProvider()
            : GetProvider(providerName);
        if (ResolveApiKey(entry) is { } apiKey)
            return CreateProvider(entry, apiKey);
        return new NullProvider();
    }

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
}
