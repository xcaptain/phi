using Phi.Avalonia.Desktop2.ViewModels;
using Phi.Providers;

namespace Phi.Avalonia.Desktop2.Tests;

/// <summary>
/// <see cref="PromptInputViewModel.SendCommand"/>: forwards the
/// current (Text, SelectedWorkspace, SelectedModel) triple to the
/// <see cref="PromptInputViewModel.SubmitCallback"/> set by the parent
/// (typically <see cref="ChatPageViewModel"/>).
/// </summary>
public class PromptInputViewModelSessionTests
{
    [Test]
    public async Task SendCommand_InvokesSubmitCallbackWithCurrentValues()
    {
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers)
        {
            SelectedWorkspace = "/tmp/work",
        };
        vm.SelectedModel = "deepseek-v4-flash";

        var captured = new List<(string text, string cwd, string model)>();
        vm.SubmitCallback = (text, cwd, model) => captured.Add((text, cwd, model));

        vm.Text = "hello world";
        vm.SendCommand.Execute(null);

        await Assert.That(captured.Count).IsEqualTo(1);
        await Assert.That(captured[0]).IsEqualTo(("hello world", "/tmp/work", "deepseek-v4-flash"));
    }

    [Test]
    public async Task SendCommand_DoesNothingWhenCallbackNotSet()
    {
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);
        vm.Text = "test";

        // SubmitCallback is null — the command should be a no-op (no
        // exception, no observable side effect on the VM).
        var beforeText = vm.Text;
        vm.SendCommand.Execute(null);

        await Assert.That(vm.Text).IsEqualTo(beforeText);
    }

    [Test]
    public async Task SendCommand_DoesNothingWhenTextIsEmpty()
    {
        var providers = CreateProviderManager();
        var vm = new PromptInputViewModel(providers);

        var fired = 0;
        vm.SubmitCallback = (_, _, _) => fired++;

        vm.SendCommand.Execute(null);

        await Assert.That(fired).IsEqualTo(0);
    }

    [Test]
    public async Task IsRunning_DrivesSendIconKindAndTooltip()
    {
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);

        await Assert.That(vm.SendIconKind).IsEqualTo(Material.Icons.MaterialIconKind.ArrowUpward);
        await Assert.That(vm.SendToolTip).IsEqualTo("Send");

        vm.IsRunning = true;
        await Assert.That(vm.SendIconKind).IsEqualTo(Material.Icons.MaterialIconKind.Stop);
        await Assert.That(vm.SendToolTip).IsEqualTo("Stop");

        vm.IsRunning = false;
        await Assert.That(vm.SendIconKind).IsEqualTo(Material.Icons.MaterialIconKind.ArrowUpward);
        await Assert.That(vm.SendToolTip).IsEqualTo("Send");
    }

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

/// <summary>
/// <see cref="PromptInputViewModel.ChooseFolderProvider"/>: the
/// sentinel "📁 Choose folder…" row triggers the OS folder picker
/// via a Func seam the view wires up. Non-sentinel clicks must also
/// flow into <see cref="PromptInputViewModel.SelectedWorkspace"/>
/// (otherwise <c>HandleSubmit</c> would submit the stale default
/// cwd). These tests use a stub provider so they don't need a
/// running Avalonia dialog stack.
/// </summary>
public class PromptInputViewModelWorkspacePickerTests
{
    [Test]
    public async Task NonSentinelRowClick_UpdatesSelectedWorkspace()
    {
        // The picker row's click flows through OnSelectedWorkspaceItemChanged
        // → SelectedWorkspace = row.Cwd. Without this, picking a non-default
        // row from the dropdown was purely cosmetic — submit would use the
        // original default cwd.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var ws = "/tmp/phi-test-picked-ws";
        var vm = new PromptInputViewModel(providers, [ws]);

        var row = vm.AvailableWorkspaces.First(r => !r.IsSentinel && r.Cwd == Path.GetFullPath(ws));
        vm.SelectedWorkspaceItem = row;

        await Assert.That(vm.SelectedWorkspace).IsEqualTo(Path.GetFullPath(ws));
    }

    [Test]
    public async Task SentinelClick_InvokesChooseFolderProvider()
    {
        // Clicking the sentinel must trigger the OS picker via the
        // injected Func. The view (PromptInputView) wires this to
        // TopLevel.StorageProvider.OpenFolderPickerAsync in production;
        // tests substitute a stub that records the call.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);
        var sentinel = vm.AvailableWorkspaces.First(r => r.IsSentinel);

        var pickerCalls = 0;
        vm.ChooseFolderProvider = () =>
        {
            pickerCalls++;
            return Task.FromResult<string?>(null);
        };

        vm.SelectedWorkspaceItem = sentinel;

        // Provider is async — give it a turn to run.
        await Task.Yield();
        await Task.Yield();

        await Assert.That(pickerCalls).IsEqualTo(1);
    }

    [Test]
    public async Task SentinelClick_SuccessfulPick_UpdatesSelectedWorkspaceToPickedPath()
    {
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);
        var sentinel = vm.AvailableWorkspaces.First(r => r.IsSentinel);

        var picked = "/tmp/phi-test-picked-via-dialog";
        vm.ChooseFolderProvider = () => Task.FromResult<string?>(picked);

        var before = vm.SelectedWorkspace;
        vm.SelectedWorkspaceItem = sentinel;

        // Wait for the picker continuation to settle. Two yields is
        // enough for a synchronous Task.FromResult — production uses
        // a real OS dialog that awaits the dispatcher.
        await Task.Yield();
        await Task.Yield();

        await Assert.That(vm.SelectedWorkspace).IsEqualTo(Path.GetFullPath(picked));
        await Assert.That(vm.SelectedWorkspace).IsNotEqualTo(before);
    }

    [Test]
    public async Task SentinelClick_CancelledPicker_LeavesSelectedWorkspaceUnchanged()
    {
        // Provider returns null (user clicked Cancel). The VM must
        // not touch SelectedWorkspace — the user's existing cwd
        // stays in force for the next submit.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);
        var sentinel = vm.AvailableWorkspaces.First(r => r.IsSentinel);

        vm.ChooseFolderProvider = () => Task.FromResult<string?>(null);

        var before = vm.SelectedWorkspace;
        vm.SelectedWorkspaceItem = sentinel;
        await Task.Yield();
        await Task.Yield();

        await Assert.That(vm.SelectedWorkspace).IsEqualTo(before);
    }

    [Test]
    public async Task SentinelClick_DoesNotChangeSelectedWorkspaceItem()
    {
        // Visual contract: clicking the sentinel shouldn't leave the
        // ComboBox visually stuck on the sentinel row while the OS
        // dialog is up. SyncSelectedItemToWorkspace snaps the visual
        // selection back to the current-cwd row.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var vm = new PromptInputViewModel(providers);
        var sentinel = vm.AvailableWorkspaces.First(r => r.IsSentinel);

        vm.ChooseFolderProvider = () => Task.FromResult<string?>(null);

        vm.SelectedWorkspaceItem = sentinel;

        // Sentinel click synchronously runs SyncSelectedItemToWorkspace
        // before kicking off the async provider — the visual selection
        // should already be reverted by the time the test asserts.
        var currentRow = vm.SelectedWorkspaceItem;
        await Assert.That(currentRow).IsNotNull();
        await Assert.That(currentRow!.IsSentinel).IsFalse();
        await Assert.That(currentRow.Cwd).IsEqualTo(vm.SelectedWorkspace);
    }

    [Test]
    public async Task SentinelClick_PickerResultIgnoredWhenUserPickedDifferentRow()
    {
        // Race: sentinel clicked, dialog opens, user picks a different
        // row while the dialog is up. When the dialog returns, the
        // user's manual choice must win. PickFolderAsync compares
        // beforeCwd to current SelectedWorkspace and bails on
        // mismatch.
        var providers = CreateProviderManager();
        providers.SetApiKey(ProviderCatalog.DeepSeek, "ds-key");

        var ws = "/tmp/phi-test-other-ws";
        var vm = new PromptInputViewModel(providers, [ws]);
        var sentinel = vm.AvailableWorkspaces.First(r => r.IsSentinel);

        var picker = new TaskCompletionSource<string?>();
        vm.ChooseFolderProvider = () => picker.Task;

        // Click sentinel — opens the (pending) picker.
        vm.SelectedWorkspaceItem = sentinel;

        // While the picker is pending, the user picks a different row.
        var otherRow = vm.AvailableWorkspaces.First(
            r => !r.IsSentinel && r.Cwd == Path.GetFullPath(ws));
        vm.SelectedWorkspaceItem = otherRow;

        await Assert.That(vm.SelectedWorkspace).IsEqualTo(Path.GetFullPath(ws));

        // Now the picker resolves with a different path — VM must ignore it.
        picker.SetResult("/tmp/phi-test-late-pick");
        await picker.Task;
        await Task.Yield();
        await Task.Yield();

        await Assert.That(vm.SelectedWorkspace).IsEqualTo(Path.GetFullPath(ws));
    }

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