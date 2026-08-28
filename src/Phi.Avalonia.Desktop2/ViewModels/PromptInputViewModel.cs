using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Bottom-row input editor for the chat. UI-3 ships with hard-coded
/// workspace / model / message lists so the input panel reads as a real
/// editor, not placeholder text. The wiring phase replaces these
/// hard-coded collections with <c>WorkspaceSessionStore.ListWorkspaces()</c>
/// / <c>ISession.AvailableProviders</c> / <c>ISession.SwitchModel</c>.
/// <para>
/// <see cref="IsRunning"/> drives the send-button icon: idle →
/// <see cref="MaterialIconKind.ArrowUp"/> (send), running →
/// <see cref="MaterialIconKind.Stop"/> (square cancel). The send button
/// itself doesn't need a new view — it just binds
/// <see cref="SendIconKind"/> as the icon source.
/// </para>
/// </summary>
public partial class PromptInputViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _text = "";

    [ObservableProperty]
    private string _selectedWorkspace = "~/work/phi";

    [ObservableProperty]
    private string _selectedModel = "claude-sonnet-4-5";

    /// <summary>
    /// True while a turn is in flight. Toggled by <see cref="SendCommand"/>;
    /// in the wiring phase the runtime's <c>SessionState.IsRunning</c> is
    /// the source of truth and this property just mirrors it.
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>Mock workspace list for UI-3. The wiring phase replaces
    /// this with <c>WorkspaceSessionStore.ListWorkspaces()</c>.</summary>
    public IReadOnlyList<string> AvailableWorkspaces { get; } =
    [
        "~/work/phi",
        "~/work/phi-frontend",
        "~/work/experiments/mini-coder",
        "~/work/personal/notes",
        "~/Downloads",
    ];

    /// <summary>Mock model list for UI-3. The wiring phase replaces this
    /// with <c>ISession.AvailableProviders</c>.</summary>
    public IReadOnlyList<string> AvailableModels { get; } =
    [
        "claude-sonnet-4-5",
        "gpt-5",
        "o1",
        "deepseek-r1",
        "llama-3-3-70b",
    ];

    /// <summary>
    /// Send-button icon. UpArrow when idle (user can hit send), Stop
    /// (filled square) when a turn is running (user can cancel). Single
    /// binding drives the icon swap.
    /// </summary>
    public MaterialIconKind SendIconKind =>
        IsRunning ? MaterialIconKind.Stop : MaterialIconKind.ArrowUp;

    /// <summary>Tooltip + accessibility label for the send button.</summary>
    public string SendToolTip =>
        IsRunning ? "Stop" : "Send";

    public IRelayCommand SendCommand { get; }

    public PromptInputViewModel()
    {
        // UI-3 polish: clicking the button just toggles the IsRunning
        // flag so we can see the icon flip. The wiring phase replaces
        // the body with active.Current.SubmitPrompt(text) and
        // active.Current.Cancel() based on IsRunning.
        SendCommand = new RelayCommand(
            execute: () => IsRunning = !IsRunning,
            canExecute: () => !string.IsNullOrWhiteSpace(Text) || IsRunning);
    }

    /// <summary>Called from the VM's partial property generator so
    /// <see cref="SendIconKind"/> / <see cref="SendToolTip"/> re-evaluate
    /// when <see cref="IsRunning"/> flips.</summary>
    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(SendIconKind));
        OnPropertyChanged(nameof(SendToolTip));
    }
}
