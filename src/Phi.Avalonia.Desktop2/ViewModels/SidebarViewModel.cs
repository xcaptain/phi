using CommunityToolkit.Mvvm.Input;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Sidebar region of the shell. Holds the two navigation commands
/// (<see cref="NewChatCommand"/> / <see cref="ShowProvidersCommand"/>)
/// the sidebar buttons bind to. Each command sets
/// <see cref="ShellViewModel.CurrentPage"/> on the parent shell, so
/// the shell hosts whichever view <c>ViewLocator</c> resolves from the
/// new VM.
/// <para>
/// SidebarViewModel holds a back-reference to its parent shell because
/// navigation is shell-owned state — the alternative is a callback /
/// event indirection that adds plumbing without buying us anything in
/// this single-shell prototype.
/// </para>
/// </summary>
public partial class SidebarViewModel(ShellViewModel shell) : ViewModelBase
{
    private readonly ShellViewModel _shell = shell;

    public IReadOnlyList<SessionEntryViewModel> Sessions { get; } =
        [];

    [RelayCommand]
    private void NewChat()
    {
        // Routed through ViewLocator → ChatPageView. Each click gets a
        // fresh VM so any in-progress input is discarded.
        _shell.CurrentPage = new ChatPageViewModel();
    }

    [RelayCommand]
    private void ShowProviders()
    {
        // Routed through ViewLocator → ProvidersPageView. Each click
        // gets a fresh VM so any in-progress key edits are discarded;
        // Phase: backend wiring will inject the composition-root
        // ProviderManager so saved keys persist across navigation.
        _shell.CurrentPage = new ProvidersPageViewModel();
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
