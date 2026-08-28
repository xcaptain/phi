using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Phi.Providers;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Root shell view-model. Owns the sidebar, the active session holder,
/// and the currently-displayed content area. <see cref="CurrentPage"/>
/// is <see cref="ViewModelBase"/> so the shell's ContentControl can
/// hold any routed VM (chat, providers, …) and <c>ViewLocator</c>
/// resolves each one to its view.
/// <para>
/// Session lifecycle:
/// <list type="bullet">
/// <item><see cref="ActiveSession.Changed"/> fires when the active
/// session is swapped (sidebar <c>New Chat</c> or future <c>Resume</c>).
/// The shell rebuilds <see cref="CurrentPage"/> with a fresh
/// <c>ChatPageViewModel</c> bound to the new session.</item>
/// <item>Sidebar <c>New Chat</c> calls <see cref="NewChatCommand"/>
/// which delegates to <c>Composition.NewSessionAsync</c> (home cwd,
/// current model picker selection). The composition layer disposes
/// the outgoing session and fires the swap event.</item>
/// </list>
/// </para>
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    public SidebarViewModel Sidebar { get; }
    public ActiveSession Active { get; }
    public ProviderManager Providers { get; }

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public ShellViewModel(ActiveSession active, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(providers);

        Active = active;
        Providers = providers;
        Sidebar = new SidebarViewModel(this);

        // Default landing page: chat, bound to the initial session
        // Composition.InitializeAsync already loaded. If no session
        // existed yet (a UI-3 follow-up where the shell boots in
        // pre-session state) ChatPageViewModel's nullable ctor handles
        // it — the prompt input shows with an empty transcript.
        var initial = active.Current;
        _currentPage = initial is null
            ? new ChatPageViewModel(providers, session: null)
            : new ChatPageViewModel(providers, initial);

        Active.Changed += OnActiveSessionChanged;
    }

    private void OnActiveSessionChanged()
    {
        // Rebuild the chat page against the new session. Each rebuild
        // subscribes to the new session's StateChanged in its ctor and
        // unsubscribes on dispose (the chat page VM is no longer
        // referenced once CurrentPage flips to a new instance).
        var next = Active.Current;
        CurrentPage = next is null
            ? new ChatPageViewModel(Providers, session: null)
            : new ChatPageViewModel(Providers, next);
    }
}