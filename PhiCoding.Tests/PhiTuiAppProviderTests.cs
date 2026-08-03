using PhiCoding.Providers;
using PhiCoding.Tests.Helpers;
using PhiCoding.Tui;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tests;

/// <summary>
/// Provider/model switching driven through <see cref="PhiCoding.Tui.PhiTuiApp"/>
/// (<c>/connect</c>, <c>/models</c>): key resolution, provider construction,
/// session switching, and default-selection persistence. Exercises the pure
/// connection logic against a <see cref="MockSession"/>; the live dialogs
/// themselves are terminal-bound and not tested here.
/// </summary>
[NotInParallel("phi-tui-provider-tests")]
public class PhiTuiAppProviderTests : IDisposable
{
    private readonly string _credentialsPath;
    private readonly string _settingsPath;
    private readonly List<IDisposable> _owned = [];

    public PhiTuiAppProviderTests()
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

    private static PhiCoding.Tui.PhiTuiApp CreateApp(MockSession session, ProviderManager manager) =>
        new(session, manager);

    private static int TranscriptItems(ChatTranscript transcript) =>
        ((DocumentFlow)transcript.Visual).Items.Count;

    [Test]
    public async Task ConnectWithKey_SwitchesProviderAndPersistsDefault()
    {
        var session = new MockSession();
        var manager = CreateManager();
        var app = CreateApp(session, manager);
        var transcript = new ChatTranscript();

        app.ConnectWithKey(ProviderCatalog.DeepSeek, "sk-123", transcript);

        await Assert.That(session.LastSwitchedProviderName).IsEqualTo("deepseek");
        await Assert.That(session.LastSwitchedModel).IsEqualTo("deepseek-v4-flash");
        await Assert.That(session.LastSwitchedProvider).IsNotNull();
        await Assert.That(session.State.ProviderName).IsEqualTo("deepseek");

        var settings = PhiSettings.Load(_settingsPath);
        await Assert.That(settings.DefaultProvider).IsEqualTo("deepseek");
        await Assert.That(settings.DefaultModel).IsEqualTo("deepseek-v4-flash");
        await Assert.That(TranscriptItems(transcript)).IsEqualTo(1);

        if (session.LastSwitchedProvider is { } provider) _owned.Add(provider);
    }

    [Test]
    public async Task ApplyApiKeyAndConnect_SavesKeyToStoreAndSwitches()
    {
        var session = new MockSession();
        var manager = CreateManager();
        var app = CreateApp(session, manager);
        var transcript = new ChatTranscript();

        app.ApplyApiKeyAndConnect(ProviderCatalog.Glm, "sk-new", transcript);

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
        var app = CreateApp(session, manager);
        var transcript = new ChatTranscript();

        app.ApplyApiKeyAndConnect(ProviderCatalog.Glm, "sk-new", transcript);

        await Assert.That(manager.ResolveApiKey(ProviderCatalog.Glm)).IsEqualTo("sk-new");
        await Assert.That(session.LastSwitchedModel).IsEqualTo("glm-4.7-flash");

        if (session.LastSwitchedProvider is { } provider) _owned.Add(provider);
    }

    [Test]
    public async Task ConnectProviderByName_UnknownProvider_AddsInfo_NoSwitch()
    {
        var session = new MockSession();
        var app = CreateApp(session, CreateManager());
        var transcript = new ChatTranscript();

        app.ConnectProviderByName("nope", transcript, new PromptEditor());

        await Assert.That(session.LastSwitchedProviderName).IsNull();
        await Assert.That(TranscriptItems(transcript)).IsEqualTo(1);
    }

    [Test]
    public async Task SwitchModelByName_NoProvider_AddsInfo_NoSwitch()
    {
        var session = new MockSession();
        var app = CreateApp(session, CreateManager());
        var transcript = new ChatTranscript();

        app.SwitchModelByName("glm-5.1", transcript);

        await Assert.That(session.LastSwitchedModel).IsNull();
        await Assert.That(TranscriptItems(transcript)).IsEqualTo(1);
    }

    [Test]
    public async Task SwitchModelByName_InvalidModel_AddsInfo_NoSwitch()
    {
        var session = new MockSession();
        session.UpdateState(s => s with { ProviderName = "glm", Model = "glm-4.7-flash" });
        var app = CreateApp(session, CreateManager());
        var transcript = new ChatTranscript();

        app.SwitchModelByName("does-not-exist", transcript);

        await Assert.That(session.LastSwitchedModel).IsNull();
        await Assert.That(TranscriptItems(transcript)).IsEqualTo(1);
    }

    [Test]
    public async Task SwitchModelByName_ValidModel_SwitchesAndPersists()
    {
        var session = new MockSession();
        session.UpdateState(s => s with { ProviderName = "glm", Model = "glm-4.7-flash" });
        var app = CreateApp(session, CreateManager());
        var transcript = new ChatTranscript();

        app.SwitchModelByName("glm-5.1", transcript);

        await Assert.That(session.LastSwitchedModel).IsEqualTo("glm-5.1");
        var settings = PhiSettings.Load(_settingsPath);
        await Assert.That(settings.DefaultProvider).IsEqualTo("glm");
        await Assert.That(settings.DefaultModel).IsEqualTo("glm-5.1");
        await Assert.That(TranscriptItems(transcript)).IsEqualTo(1);
    }
}
