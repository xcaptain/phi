using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Bottom-row input editor for the chat. UI-1 ships with empty workspace /
/// model / message dropdowns — hard-code empty collections, the wiring phase
/// pulls them from <c>WorkspaceSessionStore.ListWorkspaces()</c> /
/// <c>ISession.AvailableProviders</c> / <c>ISession.SwitchModel</c>.
/// </summary>
public partial class PromptInputViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _text = "";

    [ObservableProperty]
    private string _selectedWorkspace = "";

    [ObservableProperty]
    private string _selectedModel = "";

    public IReadOnlyList<string> AvailableWorkspaces { get; } =
        Array.Empty<string>();

    public IReadOnlyList<string> AvailableModels { get; } =
        Array.Empty<string>();

    public IRelayCommand SendCommand { get; }

    public PromptInputViewModel()
    {
        // Empty queue in UI-1; composition root wires SendCommand to
        // active.Current.SubmitPrompt(text) once the runtime is connected.
        SendCommand = new RelayCommand(
            execute: () => { },
            canExecute: () => !string.IsNullOrWhiteSpace(Text));
    }
}
