using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Phi.Avalonia.Desktop2.ViewModels;

namespace Phi.Avalonia.Desktop2.Components;

/// <summary>
/// Prompt input layout container. Layout / x:Name wiring plus the
/// OS folder picker seam. The two keyboard bits that we care about
/// — Enter submits, Shift+Enter inserts a newline — are wired in
/// XAML via <see cref="PromptInputView.axaml"/>'s
/// <c>TextBox.KeyBindings</c>: a single
/// <c>&lt;KeyBinding Gesture="Enter" Command="{Binding SendCommand}"/&gt;</c>
/// runs in the pre-RaiseEvent walk inside <c>KeyboardDevice</c>, so
/// when SendCommand.CanExecute is true the event is marked Handled
/// before TextBox's own OnKeyDown override sees the press (no
/// spurious newline on submit). Shift+Enter is intentionally not
/// bound so it falls through to TextBox.AcceptsReturn which inserts
/// the newline.
/// <para>
/// macOS Emacs-style caret keys (Ctrl-A/E/F/B/N/P/D/K/H) are
/// intentionally NOT wired here. Avalonia 12 on macOS does not
/// surface Cocoa <c>NSStandardKeyBindingResponding</c> bindings to
/// the TextBox (the keymap only maps Option+Arrow for word-jump,
/// leaving Ctrl+F/A/E without a default handler). Re-implementing
/// Cocoa bindings on our side belongs upstream in Avalonia; until
/// that's fixed the user can use macOS's <c>Cmd+Arrow</c> /
/// <c>Home</c> / <c>End</c> / <c>Cmd+Shift+Arrow</c> which Avalonia's
/// TextBox does handle natively (those go through the platform
/// keymap, not the Cocoa text bindings).
/// </para>
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
        // Wire the OS folder picker provider when the VM changes.
        // The provider is per-VM so the chat page swap automatically
        // re-binds the picker against the new VM.
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
