using CommunityToolkit.Mvvm.ComponentModel;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Root shell view-model. Owns the sidebar and the currently-displayed
/// content area. <see cref="CurrentPage"/> is <see cref="ViewModelBase"/>
/// so the shell's ContentControl can hold any routed VM (chat, providers,
/// …) and <c>ViewLocator</c> resolves each one to its view.
/// <para>
/// Navigation: the sidebar buttons bind to <see cref="SidebarViewModel"/>'s
/// <c>NewChatCommand</c> / <c>ShowProvidersCommand</c>, which set
/// <see cref="CurrentPage"/>. Each navigation produces a fresh VM so
/// in-progress state (chat input, unsaved key edits) is discarded.
/// </para>
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    public SidebarViewModel Sidebar { get; }

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public ShellViewModel()
    {
        Sidebar = new SidebarViewModel(this);

        // Default landing page: chat. Routed through ViewLocator to
        // ChatPageView.
        _currentPage = new ChatPageViewModel();
    }
}
