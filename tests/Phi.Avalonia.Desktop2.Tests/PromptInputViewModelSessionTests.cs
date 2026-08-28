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