namespace PhiCoding.Providers;

/// <summary>
/// Key-value store for provider API keys, keyed by credential name (e.g.
/// <c>"deepseek"</c>). Implementations decide how secrets are persisted —
/// the default is a plaintext 0600 file (see <see cref="FileCredentialStore"/>);
/// a future OS-keyring backend can swap in behind this interface without
/// touching callers.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1716", Justification = "Get/Set/Delete/Has are the natural store vocabulary")]
public interface ICredentialStore
{
    /// <summary>Returns the stored key for <paramref name="name"/>, or null when absent.</summary>
    string? Get(string name);

    /// <summary>Stores (or replaces) the key for <paramref name="name"/>.</summary>
    void Set(string name, string value);

    /// <summary>Removes any stored key for <paramref name="name"/>.</summary>
    void Delete(string name);

    /// <summary>True when a key is stored for <paramref name="name"/>.</summary>
    bool Has(string name);
}
