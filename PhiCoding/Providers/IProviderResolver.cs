using PhiAgent;

namespace PhiCoding.Providers;

/// <summary>
/// Resolves a runtime <see cref="IPhiProvider"/> from a provider name. Used
/// by the session factory to rebuild the live provider on resume — a
/// session's <see cref="SessionRecord"/> only stores the name, not the
/// instance, so the factory has to reconstruct it from
/// <see cref="ProviderManager"/> (or any other catalog-aware registry) to
/// recover the API key, base URL, and HTTP transport.
/// </summary>
public interface IProviderResolver
{
    /// <summary>
    /// Returns a runtime provider for the given catalog name. An empty or
    /// null <paramref name="providerName"/> means "use the default",
    /// letting callers without a recorded preference (e.g. legacy session
    /// records) still come back to life. Throws when the name is
    /// non-empty but not in the catalog. Falls back to a no-op provider
    /// when no API key is available (the TUI surfaces the underlying
    /// configuration error on the first real call, after the user has had
    /// a chance to <c>/connect</c>).
    /// </summary>
    IPhiProvider Resolve(string providerName);
}
