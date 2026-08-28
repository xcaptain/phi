using CommunityToolkit.Mvvm.ComponentModel;
using Phi.Providers;

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
/// <para>
/// Composition root thread: <see cref="ProviderManager"/> is held here so
/// the chat / providers pages share one instance — saved keys in the
/// Providers page are visible to <c>PromptInputViewModel.AvailableModels</c>
/// on the chat page without an explicit refresh. The wiring phase will
/// introduce a composition layer (<c>Composition.cs</c>) that owns both
/// the provider manager and the active session; <see cref="ShellViewModel"/>
/// will receive those instead of constructing them.
/// </para>
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    public SidebarViewModel Sidebar { get; }
    public ProviderManager Providers { get; }

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public ShellViewModel(ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        Providers = providers;
        Sidebar = new SidebarViewModel(this);

        // Default landing page: chat. Routed through ViewLocator to
        // ChatPageView.
        _currentPage = new ChatPageViewModel(Providers);
    }
}
