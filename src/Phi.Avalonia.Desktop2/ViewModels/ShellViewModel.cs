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
    [NotifyPropertyChangedFor(nameof(IsChatPage))]
    [NotifyPropertyChangedFor(nameof(ChatPage))]
    private ViewModelBase? _currentPage;

    /// <summary>True when <see cref="CurrentPage"/> is a
    /// <see cref="ChatPageViewModel"/> — used by the shell to show
    /// the pinned chat chrome (header + prompt input). The Providers
    /// page hides both. Returns false when <see cref="CurrentPage"/>
    /// is null (initial pre-session state).</summary>
    public bool IsChatPage => CurrentPage is ChatPageViewModel;

    /// <summary>The active page cast to <see cref="ChatPageViewModel"/>,
    /// or null when the current page is a different VM type (e.g.
    /// ProvidersPageViewModel) or when no page is bound yet. Used by
    /// the shell XAML to bind the header / prompt input slots — the
    /// cast gives the XAML compiler a concrete type to resolve
    /// <c>HeaderText</c> and <c>PromptInput</c> against (a direct
    /// <c>CurrentPage.X</c> binding won't compile because
    /// <c>CurrentPage</c>'s declared type is
    /// <see cref="ViewModelBase"/>).</summary>
    public ChatPageViewModel? ChatPage => CurrentPage as ChatPageViewModel;

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
        var next = Active.Current;

        // Pre-session path: the user is in the picker UI (New Chat
        // just hit) and a fresh session was just created by
        // Composition.NewSessionAsync called from ChatPageViewModel.
        // Rebuilding the chat page here would discard the pending
        // prompt text and the picker state. Hand the new session to
        // the existing pre-session chat page instead.
        if (next is not null
            && CurrentPage is ChatPageViewModel chat
            && chat.IsPreSession)
        {
            chat.AttachSession(next);
            return;
        }

        // Default: rebuild the chat page against the new session.
        // Each rebuild subscribes to the new session's StateChanged in
        // its ctor and unsubscribes on dispose (the chat page VM is
        // no longer referenced once CurrentPage flips to a new
        // instance).
        CurrentPage = next is null
            ? new ChatPageViewModel(Providers, session: null)
            : new ChatPageViewModel(Providers, next);
    }
}