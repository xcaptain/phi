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
    public async Task AvailableWorkspaces_DefaultsToHomeAndSentinel()
    {
        // With no known workspaces passed in (cold-start + tests that
        // don't seed the store), the picker still surfaces $HOME + the
        // Choose-folder sentinel so the user always has at least one
        // cwd to pick and an out-of-list escape hatch.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fullHome = string.IsNullOrEmpty(home) ? Environment.CurrentDirectory : Path.GetFullPath(home);

        await Assert.That(vm.AvailableWorkspaces.Count).IsEqualTo(2);
        await Assert.That(vm.AvailableWorkspaces[0].Cwd).IsEqualTo(fullHome);
        await Assert.That(vm.AvailableWorkspaces[0].IsCurrent).IsTrue();
        await Assert.That(vm.AvailableWorkspaces[0].IsSentinel).IsFalse();

        var sentinel = vm.AvailableWorkspaces[^1];
        await Assert.That(sentinel.IsCurrent).IsFalse();
        await Assert.That(sentinel.IsSentinel).IsTrue();
        await Assert.That(sentinel.Cwd).IsEmpty();
        await Assert.That(sentinel.Label).IsEqualTo("📁 Choose folder…");
    }

    [Test]
    public async Task AvailableWorkspaces_PrependsKnownWorkspacesInCallerOrder()
    {
        // ChatPageViewModel passes WorkspaceSessionStore.ListWorkspaces()
        // (newest-activity first). The picker prepends each row in that
        // order, then appends $HOME and the sentinel. Order matters
        // because the most-recently-used workspace appearing at the top
        // of the dropdown is the UX intent.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var ws1 = "/tmp/phi-test-ws1";
        var ws2 = "/tmp/phi-test-ws2";
        var vm = new PromptInputViewModel(providers, [ws1, ws2]);

        var rows = vm.AvailableWorkspaces;
        // ws1, ws2, [home if not in ws1/ws2], sentinel — sentinel is always last.
        await Assert.That(rows[^1].IsSentinel).IsTrue();
        await Assert.That(rows[0].Cwd).IsEqualTo(Path.GetFullPath(ws1));
        await Assert.That(rows[1].Cwd).IsEqualTo(Path.GetFullPath(ws2));

        // Every row carries a normalized Cwd (no trailing slash, no ./
        // segments) — Path.GetFullPath is idempotent so re-normalizing
        // the stored Cwd produces the same string.
        foreach (var row in rows.Where(r => !r.IsSentinel))
        {
            await Assert.That(row.Cwd).IsEqualTo(Path.GetFullPath(row.Cwd));
            await Assert.That(row.IsCurrent)
                .IsEqualTo(string.Equals(row.Cwd, vm.SelectedWorkspace, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Test]
    public async Task AvailableWorkspaces_DoesNotDuplicateHomeAlreadyInKnownList()
    {
        // The user has a session in $HOME (e.g. a quick "what files
        // are in ~" question). ListWorkspaces returns it, so the
        // picker shouldn't double-list home as both a known row and
        // the appended $HOME row.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fullHome = string.IsNullOrEmpty(home) ? Environment.CurrentDirectory : Path.GetFullPath(home);

        var vm = new PromptInputViewModel(providers, [fullHome]);

        var homeRows = vm.AvailableWorkspaces
            .Where(r => !r.IsSentinel
                && string.Equals(r.Cwd, fullHome, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await Assert.That(homeRows.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AvailableWorkspaces_DedupesCaseInsensitively()
    {
        // On Windows and macOS APFS (case-insensitive) the same path
        // can show up under different casings. The dedupe set is
        // case-insensitive so /Users/Me and /users/me collapse into
        // one row.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var lower = "/tmp/phi-test-ws";
        var upper = "/TMP/Phi-Test-WS";
        var vm = new PromptInputViewModel(providers, [lower, upper]);

        // After dedupe we should have one row for the known workspace,
        // not two. (Path.GetFullPath may also normalize the casing on
        // case-insensitive filesystems; we only assert the dedupe
        // invariant — same normalized form appears at most once.)
        var distinct = vm.AvailableWorkspaces
            .Where(r => !r.IsSentinel && r.Cwd.Contains("phi-test-ws", StringComparison.OrdinalIgnoreCase))
            .ToList();
        await Assert.That(distinct.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AvailableWorkspaces_IsCurrentFlagMarksSelectedWorkspace()
    {
        // IsCurrent drives the data template's bolding; only one row
        // should carry it at a time and it must be the row whose Cwd
        // matches SelectedWorkspace.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var ws = "/tmp/phi-test-ws-current";
        var vm = new PromptInputViewModel(providers, [ws]);

        var current = vm.AvailableWorkspaces.Where(r => r.IsCurrent).ToList();
        await Assert.That(current.Count).IsEqualTo(1);
        await Assert.That(current[0].Cwd).IsEqualTo(vm.SelectedWorkspace);
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