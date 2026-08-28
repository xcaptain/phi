using Phi.Avalonia.Desktop2.ViewModels;
using Phi.Providers;

namespace Phi.Avalonia.Desktop2.Tests;

/// <summary>
/// <see cref="PromptInputViewModel"/>: catalog-driven model picker that
/// reads from <see cref="ProviderManager.HasApiKey"/> and exposes a
/// workspace picker shaped after the old code-only layout (current cwd
/// row + a trailing sentinel for the folder picker, which is not wired
/// in this prototype).
/// </summary>
public class PromptInputViewModelTests
{
    [Test]
    public async Task AvailableModels_FiltersToProvidersWithApiKeys()
    {
        // Wire DeepSeek + GLM; leave Kimi and MiniMax disconnected.
        // AvailableModels should be exactly DeepSeek + GLM's model
        // collections, in catalog order, with duplicates removed.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");
        providers.SetApiKey(ProviderCatalog.Glm, "glm-key");

        var vm = new PromptInputViewModel(providers);

        await Assert.That(vm.AvailableModels.Select(m => m.Label))
            .IsEquivalentTo([
                "deepseek-v4-flash",
                "deepseek-v4-pro",
                "glm-4.7-flash",
                "glm-4.7",
                "glm-5-turbo",
                "glm-5.1",
                "glm-5v-turbo",
            ]);
    }

    [Test]
    public async Task SelectedModel_PicksFirstConnectedProvidersDefaultModel()
    {
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.Glm, "glm-key");

        var vm = new PromptInputViewModel(providers);

        // GLM is the first (and only) connected provider; its
        // DefaultModel is "glm-4.7-flash" per ProviderCatalog.
        await Assert.That(vm.SelectedModel).IsEqualTo("glm-4.7-flash");
        // SelectedModelItem mirrors SelectedModel so the ComboBox
        // renders the live row bold.
        await Assert.That(vm.SelectedModelItem?.Label).IsEqualTo("glm-4.7-flash");
    }

    [Test]
    public async Task SelectedModel_IsNullWhenNoProvidersConnected()
    {
        var providers = CreateProviderManager();

        var vm = new PromptInputViewModel(providers);

        await Assert.That(vm.SelectedModel).IsNull();
        await Assert.That(vm.SelectedModelItem).IsNull();
        await Assert.That(vm.AvailableModels).IsEmpty();
        // Picker placeholder tells the user the empty state means
        // "go to Providers to configure" rather than a hidden bug.
        await Assert.That(vm.ModelPickerPlaceholder)
            .IsEqualTo("no providers connected — go to Providers");
    }

    [Test]
    public async Task AvailableModels_PreservesCatalogOrder()
    {
        // Catalog order is DeepSeek, GLM, Kimi, MiniMax. Saving Kimi +
        // DeepSeek (out of order) should produce models in catalog
        // order: DeepSeek's models first, then Kimi's.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.Kimi, "kimi-key");
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);

        await Assert.That(vm.AvailableModels[0].Label).IsEqualTo("deepseek-v4-flash");
        await Assert.That(vm.AvailableModels[^1].Label).StartsWith("kimi-");
    }

    [Test]
    public async Task ModelPickerPlaceholder_IsSelectModelWhenAnyProviderConnected()
    {
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);

        await Assert.That(vm.ModelPickerPlaceholder).IsEqualTo("Select model");
    }

    [Test]
    public async Task SelectedWorkspace_DefaultsToUserHome()
    {
        var providers = CreateProviderManager();

        var vm = new PromptInputViewModel(providers);

        // Home dir on macOS/Linux is "/Users/<user>" / "/home/<user>".
        // We don't hardcode that path — we just assert it's not empty
        // and equals what Environment reports for SpecialFolder.UserProfile.
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(expected)) expected = Environment.CurrentDirectory;

        await Assert.That(vm.SelectedWorkspace).IsEqualTo(expected);
        await Assert.That(vm.SelectedWorkspace).IsNotEmpty();
    }

    [Test]
    public async Task AvailableWorkspaces_ContainsCurrentCwdAndSentinel()
    {
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);

        // Exactly two rows: the current cwd (IsCurrent=true) and a
        // trailing sentinel ("📁 Choose folder…", IsSentinel=true).
        await Assert.That(vm.AvailableWorkspaces.Count).IsEqualTo(2);

        var first = vm.AvailableWorkspaces[0];
        await Assert.That(first.IsCurrent).IsTrue();
        await Assert.That(first.IsSentinel).IsFalse();
        await Assert.That(first.Cwd).IsEqualTo(vm.SelectedWorkspace);

        var sentinel = vm.AvailableWorkspaces[1];
        await Assert.That(sentinel.IsCurrent).IsFalse();
        await Assert.That(sentinel.IsSentinel).IsTrue();
        await Assert.That(sentinel.Cwd).IsEmpty();
        await Assert.That(sentinel.Label).IsEqualTo("📁 Choose folder…");
    }

    [Test]
    public async Task SelectedWorkspaceItem_IsTheCurrentCwdRow()
    {
        var providers = CreateProviderManager();

        var vm = new PromptInputViewModel(providers);

        // ComboBox is bound to SelectedWorkspaceItem; the row matching
        // SelectedWorkspace should be the current-cwd row.
        await Assert.That(vm.SelectedWorkspaceItem).IsNotNull();
        await Assert.That(vm.SelectedWorkspaceItem!.IsCurrent).IsTrue();
        await Assert.That(vm.SelectedWorkspaceItem.IsSentinel).IsFalse();
        await Assert.That(vm.SelectedWorkspaceItem.Cwd).IsEqualTo(vm.SelectedWorkspace);
    }

    /// <summary>Build a <see cref="ProviderManager"/> backed by an
    /// in-memory credential store so the test never touches
    /// <c>~/.phi/credentials.json</c>.</summary>
    private static ProviderManager CreateProviderManager() =>
        new(new InMemoryCredentialStore());

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);

        public string? Get(string name) =>
            _data.TryGetValue(name, out var v) ? v : null;

        public void Set(string name, string value) =>
            _data[name] = value;

        public void Delete(string name) => _data.Remove(name);

        public bool Has(string name) => _data.ContainsKey(name);
    }
}