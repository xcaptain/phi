using CommunityToolkit.Mvvm.ComponentModel;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Root shell view-model. Holds the two regions of the desktop shell:
/// <see cref="Sidebar"/> on the left (New Chat / sessions / Providers)
/// and <see cref="ChatPage"/> on the right (transcript + prompt input).
/// UI-1 wires purely XAML-first with hard-code empty state; runtime
/// wiring (Phi.Session / ActiveSession / HarnessEvent) lands later via
/// the same property surface so XAML doesn't change.
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    public SidebarViewModel Sidebar { get; }
    public ChatPageViewModel ChatPage { get; }

    public ShellViewModel()
    {
        Sidebar = new SidebarViewModel();
        ChatPage = new ChatPageViewModel();
    }
}
