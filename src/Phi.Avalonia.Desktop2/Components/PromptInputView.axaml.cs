using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Phi.Avalonia.Desktop2.ViewModels;

namespace Phi.Avalonia.Desktop2.Components;

/// <summary>
/// Prompt input layout container. Pure layout / x:Name wiring plus
/// the one piece of UI logic the VM can't carry — opening the OS
/// folder picker when the user clicks the trailing "📁 Choose
/// folder…" sentinel row. The VM exposes a <c>Func&lt;Task&lt;string?&gt;&gt;</c>
/// seam (<see cref="PromptInputViewModel.ChooseFolderProvider"/>)
/// that this view implements via <see cref="TopLevel"/>'s
/// <see cref="IStorageProvider"/>; tests substitute a stub.
/// <para>
/// The slash auto-complete popup that the old
/// <c>Phi.Avalonia.Components.PromptInputView</c> wired stays pending
/// in this prototype; the <c>AutoCompleteRoot</c> Border is part of
/// the layout so a follow-up sprint can light it up without
/// touching surrounding geometry.
/// </para>
/// </summary>
public partial class PromptInputView : UserControl
{
    public PromptInputView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Wire (or unwire) the OS folder picker provider when the VM
        // changes. The provider is per-VM so the chat page swap
        // automatically re-binds the picker against the new VM.
        if (DataContext is PromptInputViewModel vm)
        {
            vm.ChooseFolderProvider = PickFolderAsync;
        }
    }

    /// <summary>Open the ambient <see cref="TopLevel"/>'s folder
    /// picker and return the user's choice as a local filesystem
    /// path. Returns null when the platform can't pick folders (e.g.
    /// browser), when no <see cref="TopLevel"/> is reachable, or
    /// when the user cancels.</summary>
    private async Task<string?> PickFolderAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { CanPickFolder: true } provider)
            return null;

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose working directory",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
