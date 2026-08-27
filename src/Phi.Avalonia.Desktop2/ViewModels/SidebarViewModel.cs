using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Sidebar region of the shell. UI-1 ships with empty sessions list
/// and stub commands (no navigation target wired yet). The
/// <see cref="NewChatCommand"/> / <see cref="ShowProvidersCommand"/>
/// slots are deliberate plumbing seams for the wiring phase — the
/// composition root wires them to <c>ISession.NewSessionAsync</c>
/// and the in-shell view-host switch, but they look like normal
/// IRelayCommands on this side.
/// </summary>
public partial class SidebarViewModel : ViewModelBase
{
    public IReadOnlyList<SessionEntryViewModel> Sessions { get; } =
        Array.Empty<SessionEntryViewModel>();

    public IRelayCommand NewChatCommand { get; }
    public IRelayCommand ShowProvidersCommand { get; }

    public SidebarViewModel()
    {
        // No-op commands in UI-1; the wiring phase replaces the lambda
        // body with `active.Replace(await active.Current.NewSessionAsync())`
        // and a ShellView page switch respectively.
        NewChatCommand = new RelayCommand(() => { });
        ShowProvidersCommand = new RelayCommand(() => { });
    }
}

/// <summary>
/// One session row in the sidebar list. UI-1 ships an empty list; the
/// shape (Title / Cwd / IsActive) lines up with future
/// <c>WorkspaceSessionStore</c> records so the data binding doesn't
/// change when wiring happens.
/// </summary>
public sealed record SessionEntryViewModel(
    string SessionId,
    string Title,
    string? Cwd,
    bool IsActive);
