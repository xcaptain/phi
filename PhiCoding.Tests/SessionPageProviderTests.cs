using PhiCoding.Tui.Pages;
using PhiCoding.Providers;
using PhiCoding.Tui.Components;
using PhiCoding.Tests.Helpers;
using PhiCoding.Tui;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tests;

/// <summary>
/// Provider/model switching driven through <see cref="SessionPage"/>
/// (<c>/connect</c>, <c>/models</c>): key resolution, provider construction,
/// session switching, and default-selection persistence. Exercises the pure
/// connection logic against a <see cref="MockSession"/>; the live dialogs
/// themselves are terminal-bound and not tested here.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class SessionPageProviderTests : IDisposable
{
    private readonly string _credentialsPath;
    private readonly string _settingsPath;
    private readonly List<IDisposable> _owned = [];

    public SessionPageProviderTests()
    {
        _credentialsPath = Path.Combine(
            Path.GetTempPath(), "phi-tui-cred-" + Guid.NewGuid().ToString("N") + ".json");
        _settingsPath = Path.Combine(
            Path.GetTempPath(), "phi-tui-settings-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var disposable in _owned) disposable.Dispose();
        if (File.Exists(_credentialsPath)) File.Delete(_credentialsPath);
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private ProviderManager CreateManager() => new(
        new FileCredentialStore(_credentialsPath),
        _settingsPath,
        _ => null);

    private static SessionPage CreatePage(MockSession session, ProviderManager manager)
    {
        var page = new SessionPage(session, new FakeSessionNavigator(session), manager);
        page.Build();
        return page;
    }

    private static int TranscriptItems(ChatTranscript transcript) =>
        ((DocumentFlow)transcript.Visual).Items.Count;

    [Test]
    public async Task ConnectWithKey_SwitchesProviderAndPersistsDefault()
    {
        var session = new MockSession();
        var manager = CreateManager();
        var page = CreatePage(session, manager);

        page.Input.ConnectWithKey(ProviderCatalog.DeepSeek, "sk-123");

        await Assert.That(session.LastSwitchedProviderName).IsEqualTo("deepseek");
        await Assert.That(session.LastSwitchedModel).IsEqualTo("deepseek-v4-flash");
        await Assert.That(session.LastSwitchedProvider).IsNotNull();
        await Assert.That(session.State.ProviderName).IsEqualTo("deepseek");

        var settings = PhiSettings.Load(_settingsPath);
        await Assert.That(settings.DefaultProvider).IsEqualTo("deepseek");
        await Assert.That(settings.DefaultModel).IsEqualTo("deepseek-v4-flash");
        await Assert.That(TranscriptItems(page.Transcript)).IsEqualTo(1);

        if (session.LastSwitchedProvider is { } provider) _owned.Add(provider);
    }

    [Test]
    public async Task ApplyApiKeyAndConnect_SavesKeyToStoreAndSwitches()
    {
        var session = new MockSession();
        var manager = CreateManager();
        var page = CreatePage(session, manager);

        page.Input.ApplyApiKeyAndConnect(ProviderCatalog.Glm, "sk-new");

        // The entered key lands in the credential store so a later launch can
        // resolve it without the env var.
        await Assert.That(manager.ResolveApiKey(ProviderCatalog.Glm)).IsEqualTo("sk-new");
        await Assert.That(session.LastSwitchedProviderName).IsEqualTo("glm");
        await Assert.That(session.LastSwitchedModel).IsEqualTo("glm-4.7-flash");

        if (session.LastSwitchedProvider is { } provider) _owned.Add(provider);
    }

    [Test]
    public async Task ApplyApiKeyAndConnect_ReplacesExistingStoredKey()
    {
        var session = new MockSession();
        var manager = CreateManager();
        manager.SetApiKey(ProviderCatalog.Glm, "sk-old");
        var page = CreatePage(session, manager);

        page.Input.ApplyApiKeyAndConnect(ProviderCatalog.Glm, "sk-new");

        await Assert.That(manager.ResolveApiKey(ProviderCatalog.Glm)).IsEqualTo("sk-new");
        await Assert.That(session.LastSwitchedModel).IsEqualTo("glm-4.7-flash");

        if (session.LastSwitchedProvider is { } provider) _owned.Add(provider);
    }

    [Test]
    public async Task ConnectProviderByName_UnknownProvider_AddsInfo_NoSwitch()
    {
        var session = new MockSession();
        var page = CreatePage(session, CreateManager());

        page.Input.ConnectProviderByName("nope");

        await Assert.That(session.LastSwitchedProviderName).IsNull();
        await Assert.That(TranscriptItems(page.Transcript)).IsEqualTo(1);
    }

    [Test]
    public async Task ConnectWithModel_SwitchesProviderWithChosenModel_AndPersists()
    {
        var session = new MockSession();
        var manager = CreateManager();
        var page = CreatePage(session, manager);

        page.Input.ConnectWithModel(ProviderCatalog.Kimi, "sk-kimi", "kimi-k2-thinking");

        await Assert.That(session.LastSwitchedProviderName).IsEqualTo("kimi");
        await Assert.That(session.LastSwitchedModel).IsEqualTo("kimi-k2-thinking");
        await Assert.That(session.State.ProviderName).IsEqualTo("kimi");

        var settings = PhiSettings.Load(_settingsPath);
        await Assert.That(settings.DefaultProvider).IsEqualTo("kimi");
        await Assert.That(settings.DefaultModel).IsEqualTo("kimi-k2-thinking");
        await Assert.That(TranscriptItems(page.Transcript)).IsEqualTo(1);

        if (session.LastSwitchedProvider is { } provider) _owned.Add(provider);
    }

    [Test]
    public async Task BuildModelPicker_GroupsByProvider_WithHeadersAndMap()
    {
        var providers = new[] { ProviderCatalog.DeepSeek, ProviderCatalog.Glm };

        var (items, map) = PromptInput.BuildModelPicker(providers, "deepseek", "deepseek-v4-flash");

        // header + 2 models + header + 5 models
        await Assert.That(items.Count).IsEqualTo(1 + 2 + 1 + 5);
        await Assert.That(map.Count).IsEqualTo(items.Count);

        await Assert.That(items[0].IsEnabled).IsFalse();
        await Assert.That(items[0].Label).IsEqualTo("  DeepSeek");
        await Assert.That(map[0]).IsNull();

        await Assert.That(map[1]!.Value.Entry.Name).IsEqualTo("deepseek");
        await Assert.That(map[1]!.Value.Model).IsEqualTo("deepseek-v4-flash");

        await Assert.That(items[3].IsEnabled).IsFalse();
        await Assert.That(map[3]).IsNull();
        await Assert.That(map[4]!.Value.Entry.Name).IsEqualTo("glm");
        await Assert.That(map[4]!.Value.Model).IsEqualTo("glm-4.7-flash");
    }

    [Test]
    public async Task BuildModelPicker_MarksCurrentModel_OnCurrentProvider()
    {
        var (items, map) = PromptInput.BuildModelPicker([ProviderCatalog.Glm], "glm", "glm-5.1");

        // header(0), glm-4.7-flash(1), glm-4.7(2), glm-5-turbo(3), glm-5.1(4), glm-5v-turbo(5)
        await Assert.That(items[4].Label).IsEqualTo("  ✓ glm-5.1");
        await Assert.That(items[1].Label).IsEqualTo("    glm-4.7-flash");
        await Assert.That(items[1].IsEnabled).IsTrue();
        await Assert.That(map[4]!.Value.Entry.Name).IsEqualTo("glm");
    }

    [Test]
    public async Task BuildModelPickerProviders_IncludesCurrentEvenWithoutKey()
    {
        var providers = PromptInput.BuildModelPickerProviders(
            ProviderCatalog.All, "kimi", _ => false);

        await Assert.That(providers.Select(p => p.Name)).IsEquivalentTo(["kimi"]);
    }

    [Test]
    public async Task BuildModelPickerProviders_ConfiguredAndCurrent_Deduplicated()
    {
        var hasKey = new Func<ProviderCatalogEntry, bool>(e => e.Name is "deepseek" or "glm" or "kimi");

        var providers = PromptInput.BuildModelPickerProviders(ProviderCatalog.All, "kimi", hasKey);

        await Assert.That(providers.Select(p => p.Name)).IsEquivalentTo(["deepseek", "glm", "kimi"]);
        await Assert.That(providers.Count).IsEqualTo(3);
    }

    [Test]
    public async Task FormatProviderLabel_CurrentWithKey_ShowsCheckAndModel()
    {
        var label = PromptInput.FormatProviderLabel(
            ProviderCatalog.DeepSeek, "deepseek", hasKey: true, "deepseek-v4-flash");

        await Assert.That(label).IsEqualTo("  ✓ DeepSeek — deepseek · deepseek-v4-flash");
    }

    [Test]
    public async Task FormatProviderLabel_OtherWithoutKey_MarksNoKey()
    {
        var label = PromptInput.FormatProviderLabel(
            ProviderCatalog.Kimi, "deepseek", hasKey: false, null);

        await Assert.That(label).IsEqualTo("    Moonshot Kimi — kimi  (no key)");
    }

    [Test]
    public async Task FormatProviderLabel_OtherWithKey_PlainRow()
    {
        var label = PromptInput.FormatProviderLabel(
            ProviderCatalog.Glm, "deepseek", hasKey: true, "deepseek-v4-flash");

        await Assert.That(label).IsEqualTo("    Zhipu GLM — glm");
    }
}
