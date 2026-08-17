using PhiCoding.Providers;
using PhiProvider;

namespace PhiCoding.Tests;

public class ProviderManagerTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly List<string> _tempPaths = [];

    public ProviderManagerTests()
    {
        _settingsPath = Path.Combine(
            Path.GetTempPath(), "phi-manager-" + Guid.NewGuid().ToString("N") + ".json");
        _tempPaths.Add(_settingsPath);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var path in _tempPaths)
            if (File.Exists(path)) File.Delete(path);
    }

    private ProviderManager CreateManager(ICredentialStore? store = null) => new(
        store ?? new FileCredentialStore(_tempPath("credentials.json")),
        _settingsPath);

    private static string _tempPath(string fileName) =>
        Path.Combine(Path.GetTempPath(), "phi-manager-" + Guid.NewGuid().ToString("N") + "-" + fileName);

    [Test]
    public async Task GetProvider_Known_ReturnsEntry()
    {
        var manager = CreateManager();
        await Assert.That(manager.GetProvider("deepseek").Name).IsEqualTo("deepseek");
        await Assert.That(manager.GetProvider("DEEPSEEK").Name).IsEqualTo("deepseek");
    }

    [Test]
    public async Task GetProvider_Unknown_Throws()
    {
        var manager = CreateManager();
        await Assert.That(() => manager.GetProvider("nope")).Throws<ArgumentException>();
    }

    [Test]
    public async Task ResolveApiKey_FromStore()
    {
        // API keys are now exclusively sourced from the credential store;
        // no env-var fallback. Set on the store and read back through the
        // manager.
        var store = new FileCredentialStore(_tempPath("c.json"));
        store.Set("deepseek", "from-store");

        var manager = CreateManager(store);
        await Assert.That(manager.ResolveApiKey(ProviderCatalog.DeepSeek)).IsEqualTo("from-store");
    }

    [Test]
    public async Task ResolveApiKey_StoreUsedWhenNoEnv()
    {
        var store = new FileCredentialStore(_tempPath("c.json"));
        store.Set("deepseek", "from-store");

        var manager = CreateManager(store);
        await Assert.That(manager.ResolveApiKey(ProviderCatalog.DeepSeek)).IsEqualTo("from-store");
    }

    [Test]
    public async Task ResolveApiKey_None_ReturnsNull()
    {
        var manager = CreateManager();
        await Assert.That(manager.ResolveApiKey(ProviderCatalog.DeepSeek)).IsNull();
    }

    [Test]
    public async Task HasApiKey_ReflectsStore()
    {
        var store = new FileCredentialStore(_tempPath("c.json"));
        var manager = CreateManager(store);

        await Assert.That(manager.HasApiKey(ProviderCatalog.DeepSeek)).IsFalse();
        store.Set("deepseek", "k");
        await Assert.That(manager.HasApiKey(ProviderCatalog.DeepSeek)).IsTrue();
    }

    [Test]
    public async Task GetApiKey_Missing_ThrowsWithActionableHint()
    {
        var manager = CreateManager();
        var ex = Assert.Throws<InvalidOperationException>(
            () => manager.GetApiKey(ProviderCatalog.DeepSeek));
        // The message points the user at the in-app provisioning flows
        // (TUI slash command + desktop Providers page); we no longer hint
        // at env vars since keys come exclusively from the credential
        // store now.
        await Assert.That(ex.Message).Contains("deepseek");
        await Assert.That(ex.Message).Contains("/connect");
    }

    [Test]
    public async Task CreateProvider_AnthropicKind_BuildsAnthropicProvider()
    {
        var manager = CreateManager();
        using var provider = manager.CreateProvider(ProviderCatalog.DeepSeek, "k");
        await Assert.That(provider).IsTypeOf<AnthropicProvider>();
    }

    [Test]
    public async Task CreateProvider_OpenAIKind_BuildsOpenAICompatibleProvider()
    {
        var manager = CreateManager();
        using var provider = manager.CreateProvider(ProviderCatalog.Glm, "k");
        await Assert.That(provider).IsTypeOf<OpenAICompatibleProvider>();
    }

    [Test]
    public async Task SaveDefaultAndResolve_RoundTrip()
    {
        var manager = CreateManager();
        manager.SaveDefault(ProviderCatalog.Glm, "glm-5.1");

        await Assert.That(manager.ResolveDefaultProvider().Name).IsEqualTo("glm");
        await Assert.That(manager.ResolveDefaultModel(ProviderCatalog.Glm)).IsEqualTo("glm-5.1");
    }

    [Test]
    public async Task ResolveDefaultProvider_NoSettings_FallsBackToFirstEntry()
    {
        var manager = CreateManager();
        await Assert.That(manager.ResolveDefaultProvider().Name).IsEqualTo("deepseek");
    }

    [Test]
    public async Task ResolveDefaultProvider_PersistedName_RespectedAcrossInstances()
    {
        var manager = CreateManager();
        manager.SaveDefault(ProviderCatalog.Glm, "glm-5.1");

        var other = CreateManager();
        await Assert.That(other.ResolveDefaultProvider().Name).IsEqualTo("glm");
    }

    [Test]
    public async Task ResolveDefaultModel_InvalidPersistedModel_FallsBackToProviderDefault()
    {
        var manager = CreateManager();
        manager.SaveDefault(ProviderCatalog.Glm, "glm-5.1");

        await Assert.That(manager.ResolveDefaultModel(ProviderCatalog.Kimi)).IsEqualTo("kimi-k2.7-code");
    }
}
