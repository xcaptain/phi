using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Phi.Avalonia.Desktop2.Components;

namespace Phi.Avalonia.Desktop2.ViewModels;

/// <summary>
/// Sidebar region of the shell. Owns the navigation commands (New
/// Chat / Show Providers), the group-mode toggle (ByDate /
/// ByWorkspace), and the live entries list the sidebar renders.
/// <para>
/// The entries list is rebuilt from <c>WorkspaceSessionStore</c> on:
/// <list type="bullet">
/// <item><see cref="Active.Changed"/> (new session created / session
/// resumed — list re-reads, IsActive flag flips).</item>
/// <item>Group-mode toggle — list re-orders.</item>
/// <item>Watched current session's <see cref="Phi.ISession.StateChanged"/>:
/// when <c>IsPersisted</c> flips false→true (the first message
/// persists the session record) or <c>SessionTitle</c> changes (LLM
/// auto-namer fills in the real title), the list rebuilds so the new
/// entry replaces the id-prefix placeholder.</item>
/// </list>
/// </para>
/// <para>
/// Click on a session row fires <see cref="SelectSessionCommand"/> which
/// calls <c>ISession.ResumeAsync(id)</c> on the active session; that
/// in turn fires <see cref="Active.Changed"/> and we rebuild again.
/// </para>
/// </summary>
public partial class SidebarViewModel : ViewModelBase
{
    private readonly ShellViewModel _shell;

    /// <summary>The flat list rendered by the sidebar. Mix of
    /// <see cref="WorkspaceNavEntryViewModel"/> + <see cref="SessionNavEntryViewModel"/>;
    /// the XAML's per-kind DataTemplate handles visual dispatch.</summary>
    public ObservableCollection<NavEntryViewModel> Entries { get; } = new();

    /// <summary>Current group mode. Default
    /// <see cref="NavModel.GroupMode.ByWorkspace"/>; the older shell
    /// opened in workspace mode too.</summary>
    [ObservableProperty]
    private NavModel.GroupMode _groupMode = NavModel.GroupMode.ByWorkspace;

    /// <summary>Mirrors <see cref="GroupMode"/> for the ByDate toggle
    /// button's visual binding. Computed once at property-set time so
    /// the converter doesn't need to read the VM.</summary>
    public bool ByDateActive => GroupMode == NavModel.GroupMode.ByDate;

    /// <summary>Mirror of <see cref="GroupMode"/> for the ByWorkspace
    /// toggle button's visual binding.</summary>
    public bool ByWorkspaceActive => GroupMode == NavModel.GroupMode.ByWorkspace;

    private Phi.ISession? _watchedSession;
    private bool _wasPersisted;
    private string? _lastTitle;

    public SidebarViewModel(ShellViewModel shell)
    {
        _shell = shell;
        _shell.Active.Changed += OnActiveChanged;

        // Watch the initial session's StateChanged so persisted/title
        // flips rebuild the nav. The handler swaps cleanly when the
        // active session changes (see WatchSession).
        WatchSession(_shell.Active.Current);

        RebuildNav();
    }
    [RelayCommand]
    private void NewChat()
    {
        // page with session=null, the workspace picker + model picker
        // become visible (PromptInput.IsWorkspaceLocked stays false),
        // and the user picks cwd + model + writes their first prompt.
        // The session is materialised on submit, not here — this
        // avoids creating a session the user might never use and lets
        // the picker choices actually drive the new session.
        //
        // Guard: if no provider is connected, leave the current page
        // alone; the chat page VM already surfaces this in the picker
        // placeholder ("no providers connected — go to Providers"). A
        // future UX shows a transient toast instead.
        var hasAnyConnected = Phi.Providers.ProviderCatalog.All.Any(_shell.Providers.HasApiKey);
        if (!hasAnyConnected) return;

        _shell.Active.Clear();
    }

    [RelayCommand]
    private void ShowProviders()
    {
        // Routed through ViewLocator → ProvidersPageView. Each click
        // gets a fresh VM so any in-progress key edits are discarded.
        _shell.CurrentPage = new ProvidersPageViewModel();
    }

    [RelayCommand]
    private void SetGroupMode(NavModel.GroupMode mode)
    {
        if (GroupMode == mode) return;
        GroupMode = mode;
        OnPropertyChanged(nameof(ByDateActive));
        OnPropertyChanged(nameof(ByWorkspaceActive));
        RebuildNav();
    }

    /// <summary>Delete a session row's underlying record. Removes the
    /// <c>index.jsonl</c> entry + the transcript file via
    /// <see cref="Phi.WorkspaceSessionStore.DeleteSession"/>. If the
    /// deleted session was the active one, navigates to a fresh
    /// session so the chat page keeps a valid ISession bound
    /// (otherwise the active session would still reference a record
    /// that's been wiped). Else just rebuilds the nav list so the
    /// row disappears.</summary>
    [RelayCommand]
    private async Task DeleteSessionAsync(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;

        var wasActive = _shell.Active.Current?.Id == sessionId;
        try
        {
            Phi.WorkspaceSessionStore.DeleteSession(sessionId);
        }
        catch (Exception ex)
        {
            // WorkspaceSessionStore is silent on unknown id (treats as
            // no-op); any other failure (file permissions, disk)
            // surfaces via the same console.Error sink the other nav
            // commands use until the status bar lands.
            System.Console.Error.WriteLine($"DeleteSession({sessionId}): {ex.Message}");
            return;
        }

        if (wasActive)
        {
            // The active session in memory still references the deleted
            // record. Replace it with a fresh session in the same cwd
            // (mirrors what happens when you delete the active chat in
            // most chat apps). We pick a cwd from the active session
            // before we dispose the new one for any side effects; in
            // this prototype cwd comes from the previous session.
            var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(cwd)) cwd = Environment.CurrentDirectory;

            var firstConnected = Phi.Providers.ProviderCatalog.All
                .Where(_shell.Providers.HasApiKey)
                .Cast<Phi.Providers.ProviderCatalogEntry?>()
                .FirstOrDefault();

            if (firstConnected is null) return; // No provider — leave nav as-is.
            await Composition.NewSessionAsync(
                cwd, firstConnected.Name, firstConnected.DefaultModel);
        }
        // For non-active deletes the Active.Changed → RebuildNav path
        // is unnecessary; the on-disk removal is reflected in the next
        // RebuildNav call. We trigger one explicitly so the row goes
        // away immediately rather than waiting for the next nav trigger.
        // Active.Changed already fired for active-deletes (via
        // NewSessionAsync → Replace) so RebuildNav has run there.
        if (!wasActive)
            RebuildNav();
    }

    /// <summary>Select a session row: resume it via the active session's
    /// <see cref="Phi.ISession.ResumeAsync"/>. The swap fires
    /// <see cref="Active.Changed"/> which the sidebar subscribes to so
    /// the highlight + chat page rebuild.</summary>
    [RelayCommand]
    private async Task SelectSessionAsync(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        try
        {
            // Pre-session path (shell started fresh with no live
            // session — picker UI showing) has no active session to
            // chain ISession.ResumeAsync off. Composition.ResumeSessionAsync
            // does the load + Active.Replace directly so the
            // pre-session chat page picks up the resumed session via
            // ShellViewModel.OnActiveSessionChanged (which routes
            // pre-session chats to AttachSession).
            if (_shell.Active.Current is null)
            {
                await Composition.ResumeSessionAsync(sessionId);
                return;
            }

            var next = await _shell.Active.Current.ResumeAsync(sessionId);
            _shell.Active.Replace(next);
        }
        catch (Exception ex)
        {
            // NavModel.BuildNavEntries ignores missing sessions; a
            // failed resume leaves the nav as-is. The exception is
            // swallowed here because UI prototype scope doesn't ship
            // a toast slot yet — surface via DeskLog once the status
            // bar lands.
            System.Console.Error.WriteLine($"SelectSessionAsync: {ex.Message}");
        }
    }

    private void OnActiveChanged()
    {
        // The active session was replaced (sidebar New Chat, or
        // ResumeAsync swap). Swap the watcher and rebuild the nav so
        // the new active entry's row carries IsActive=true.
        WatchSession(_shell.Active.Current);
        RebuildNav();
    }

    /// <summary>Subscribe the new active session's
    /// <see cref="Phi.ISession.StateChanged"/> so persisted / title
    /// transitions rebuild the nav. Unsubscribes the previous watcher
    /// first so we don't leak handlers across session swaps.</summary>
    private void WatchSession(Phi.ISession? session)
    {
        if (ReferenceEquals(_watchedSession, session)) return;
        if (_watchedSession is not null)
            _watchedSession.StateChanged -= OnSessionStateForNav;
        _watchedSession = session;
        if (session is null) return;
        _wasPersisted = session.State.IsPersisted;
        _lastTitle = session.State.SessionTitle;
        session.StateChanged += OnSessionStateForNav;
    }

    private void OnSessionStateForNav(Phi.SessionState state)
    {
        if (!_wasPersisted && state.IsPersisted)
        {
            _wasPersisted = true;
            RebuildNav();
            return;
        }
        if (!string.Equals(_lastTitle, state.SessionTitle, StringComparison.Ordinal))
        {
            _lastTitle = state.SessionTitle;
            RebuildNav();
        }
    }

    /// <summary>Re-read <c>WorkspaceSessionStore</c> and rebuild the
    /// entries list. Cheap — the store scans a few
    /// <c>index.jsonl</c> files under <c>~/.phi/sessions</c> and the
    /// list is bounded by the recent-N filter (default 7 days).</summary>
    private void RebuildNav()
    {
        var sessions = Phi.WorkspaceSessionStore.ListAllSessions();
        var entries = NavModel.BuildNavEntries(sessions, GroupMode);
        var activeId = _shell.Active.Current?.Id;

        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry.Kind switch
            {
                NavModel.Kind.Workspace => new WorkspaceNavEntryViewModel(entry.Title, entry.Cwd!),
                NavModel.Kind.Session => new SessionNavEntryViewModel(
                    entry.Title, entry.SessionId!, isActive: entry.SessionId == activeId),
                _ => throw new InvalidOperationException($"Unknown NavModel.Kind: {entry.Kind}"),
            });
        }
    }

    // ──────── Converter for the XAML session-row visuals ────────

    /// <summary>Boolean→IBrush + FontWeight converters for the session
    /// row's visual state when <c>IsActive</c> flips. The selected row
    /// uses FluentTheme's <c>SystemControlHighlightListMediumBrush</c> —
    /// the same brush FluentTheme paints on selected list items
    /// everywhere else (SidebarItem, MenuItem, etc.). On top of the
    /// dark sidebar (~#1F1F1F) this reads as the Fluent "elevated list
    /// row" cue; on the light sidebar it reads as a soft tint.
    /// <para>
    /// Lives at namespace scope (not nested under SidebarViewModel)
    /// because the Avalonia XAML compiler doesn't support the
    /// <c>OuterClass+NestedClass</c> lookup in <c>x:Static</c>.
    /// </para>
    /// <para>
    /// The previous ToggleBrushes converter (active → accent fill,
    /// inactive → transparent) is gone: the ByDate / ByWorkspace
    /// buttons in SidebarView use <see cref="ToggleButton"/> with
    /// <c>IsChecked</c> TwoWay binding, which gets the accent fill
    /// from FluentTheme's built-in ToggleButton styles. We don't have
    /// to ship a converter for a state FluentTheme already styles.
    /// </para>
    /// </summary>
    public static class SessionRowBrushes
    {
        // Active row → SystemControlHighlightListMediumBrush (Fluent's
        // selected-list-item fill). Inactive row → transparent so the
        // button's built-in pointerover tint shows through.
        public static readonly IValueConverter Background =
            new FuncValueConverter<bool, IBrush>(active =>
                active
                    ? LookupBrush("SystemControlHighlightListMediumBrush")
                    : Brushes.Transparent);

        // Selected row carries Medium weight per Fluent's "selected
        // list item" pattern; inactive rows stay Normal. No bold;
        // the selection reads through the background fill alone.
        public static readonly IValueConverter FontWeight =
            new FuncValueConverter<bool, global::Avalonia.Media.FontWeight>(active =>
                active ? global::Avalonia.Media.FontWeight.Medium : global::Avalonia.Media.FontWeight.Normal);
    }

    /// <summary>Resolve a brush by resource key through
    /// <see cref="Application.Current"/>'s resource dictionary. FluentTheme
    /// already populates the dictionary with every system brush under
    /// both Light and Dark ThemeDictionaries, so the lookup walks the
    /// active ThemeVariant and returns the right value. Falls back to
    /// <see cref="Brushes.Transparent"/> when the key is missing (e.g.
    /// unit tests where no Avalonia app is constructed); the conversion
    /// is silent rather than throwing so XAML load doesn't blow up on a
    /// misconfigured key.</summary>
    private static IBrush LookupBrush(string key)
    {
        if (Application.Current is { } app
            && ResourceNodeExtensions.TryFindResource(app, key, out var resource)
            && resource is IBrush brush)
        {
            return brush;
        }
        return Brushes.Transparent;
    }
}