using CommunityToolkit.Mvvm.ComponentModel;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Right-column chat region: header (session title / cwd / model chip),
/// transcript (empty placeholder in UI-1; per-line rendering lands in
/// UI-2), prompt input, status bar.
/// </summary>
public partial class ChatPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _headerText = "New Chat";

    public IReadOnlyList<object> Transcript { get; } =
        Array.Empty<object>();

    public PromptInputViewModel PromptInput { get; }
    public StatusBarViewModel StatusBar { get; }

    public ChatPageViewModel()
    {
        PromptInput = new PromptInputViewModel();
        StatusBar = new StatusBarViewModel();
    }
}
