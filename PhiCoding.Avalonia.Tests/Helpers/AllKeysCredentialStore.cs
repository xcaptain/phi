using PhiCoding.Providers;

namespace PhiCoding.Avalonia.Tests.Helpers;

/// <summary>
/// Test <see cref="ICredentialStore"/> that pretends to hold an API
/// key for every credential name. Lets <see cref="ProviderManager"/>'s
/// <c>HasApiKey</c> return true without depending on real env vars or the
/// user's local credentials file — important for CI runners that don't
/// ship provider keys and for hermetic tests that must not touch disk.
/// </summary>
internal sealed class AllKeysCredentialStore : ICredentialStore
{
    public string? Get(string name) => "test-key";
    public void Set(string name, string value) { }
    public void Delete(string name) { }
    public bool Has(string name) => true;
}