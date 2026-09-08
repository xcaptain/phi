using Phi;
using Phi.Agent;
using Phi.Providers;

namespace Phi.Avalonia.Desktop;

/// <summary>
/// Composition root for the Avalonia desktop shell. Owns the cross-process
/// singletons (provider manager, session environment, active session
/// holder) and exposes the single <see cref="InitializeAsync"/> entry point
/// the application must call before showing the UI.
/// <para>
/// Lifecycle:
/// <list type="number">
/// <item><see cref="InitializeAsync"/> is awaited from <c>Program.Main</c>
/// before <c>StartWithClassicDesktopLifetimeAsync</c> starts the UI loop.
/// It constructs the <see cref="ProviderManager"/>, builds a default
/// <see cref="SessionEnvironment"/>, and constructs an empty
/// <see cref="ActiveSession"/> (no live session yet — the chat page
/// renders in pre-session state with the workspace + model pickers
/// visible).</item>
/// <item>Frontends (<c>ShellViewModel</c>, sidebar, prompt input) read
/// <see cref="ActiveSession"/> and <see cref="Providers"/> to subscribe
/// to session swaps and to enumerate available models.</item>
/// <item><see cref="NewSessionAsync"/> creates a fresh session via
/// <see cref="Session.LoadAsync"/>, swaps it into
/// <see cref="ActiveSession"/>, and returns the new instance. The
/// prompt input's <c>HandleSubmit</c> calls into here on first submit.</item>
/// <item><see cref="ResumeSessionAsync"/> loads an existing session by
/// id and swaps it into <see cref="ActiveSession"/>. Sidebar session-row
/// selection calls into here when the shell is in pre-session state
/// (no live session to chain <c>ISession.ResumeAsync</c> off).</item>
/// </list>
/// </para>
/// <para>
/// UI: the shell starts in pre-session state (the prompt input picker
/// UI is visible). The user picks cwd + model + types their first
/// prompt, and submit materialises the session. Previously InitializeAsync
/// eagerly created an initial session so the chat page rendered content
/// on first paint, but that meant the picker was hidden until the user
/// clicked "New Chat" — the wrong default.
/// </para>
/// </summary>
public static class Composition
{
    private static SessionEnvironment? _environment;
    private static ProviderManager? _providers;
    private static ActiveSession? _activeSession;

    /// <summary>The session holder the shell binds to. Null until
    /// <see cref="InitializeAsync"/> completes.</summary>
    public static ActiveSession ActiveSession =>
        _activeSession ?? throw new InvalidOperationException(
            "Composition.ActiveSession accessed before InitializeAsync completed");

    /// <summary>The shared provider manager (resolves keys, lists
    /// connected providers' models, persists credentials). Exposed so
    /// the prompt input can filter <c>AvailableModels</c> from one
    /// instance shared with the Providers page.</summary>
    public static ProviderManager Providers =>
        _providers ?? throw new InvalidOperationException(
            "Composition.Providers accessed before InitializeAsync completed");

    /// <summary>
    /// Boots the composition: constructs <see cref="ProviderManager"/>
    /// (reads <c>~/.phi/credentials.json</c>), builds the default
    /// <see cref="SessionEnvironment"/>, and creates an empty
    /// <see cref="ActiveSession"/>. The shell starts in pre-session
    /// state — the picker UI is visible until the user submits a
    /// prompt or selects an existing session from the sidebar.
    /// Idempotent — calling twice returns without rebuilding.
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> when no provider
    /// has an API key configured. The caller (Program.Main) surfaces the
    /// failure via the OS-level unhandled exception dialog; a UI-3
    /// follow-up will catch this and redirect to the Providers page
    /// with a "no providers connected" hint.
    /// </para>
    /// </summary>
    public static async Task InitializeAsync()
    {
        if (_activeSession is not null) return;

        _providers = new ProviderManager();

        // Validate at least one provider is connected. The shell
        // surfaces the no-provider case in the prompt input picker
        // placeholder, but we want a clean bootstrap-time failure
        // when the install is misconfigured (no saved keys).
        var firstConnected = ProviderCatalog.All
            .Where(_providers.HasApiKey)
            .Cast<ProviderCatalogEntry?>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No provider has an API key configured. " +
                "Open the Providers page and save a key before starting a chat.");

        _environment = SessionEnvironment.Default(_providers);

        // Pre-session start: ActiveSession is created with no live
        // session. The user lands on the picker UI and the actual
        // session is materialised on first submit
        // (ChatPageViewModel.HandleSubmit → Composition.NewSessionAsync)
        // or on sidebar selection of an existing session
        // (Composition.ResumeSessionAsync).
        _activeSession = new ActiveSession();
    }

    /// <summary>
    /// Creates a fresh session in <paramref name="cwd"/> using
    /// <paramref name="provider"/> + <paramref name="model"/>, swaps it
    /// into <see cref="ActiveSession"/>, disposes the outgoing session,
    /// and returns the new instance. The prompt input's
    /// <c>HandleSubmit</c> calls into here on first submit (when the
    /// shell is in pre-session state).
    /// <para>
    /// Always uses <see cref="Session.LoadAsync"/> directly (rather than
    /// <see cref="ISession.NewSessionAsync"/>) so the new session takes
    /// the chosen provider/model — <c>NewSessionAsync</c> on the live
    /// session inherits the outgoing session's provider/model which is
    /// the opposite of what the picker wants.
    /// </para>
    /// </summary>
    public static async Task<ISession> NewSessionAsync(
        string cwd,
        string provider,
        string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cwd);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        if (_environment is null || _activeSession is null)
            throw new InvalidOperationException(
                "Composition.NewSessionAsync called before InitializeAsync");

        var next = await Session.LoadAsync(cwd, _environment, provider, model);
        var outgoing = _activeSession.Current;
        _activeSession.Replace(next);
        outgoing?.Dispose();
        return next;
    }

    /// <summary>
    /// Resumes an existing session by id, swaps it into
    /// <see cref="ActiveSession"/>, and returns it. Used by sidebar
    /// session-row selection when the shell is in pre-session state
    /// (no live <see cref="ISession"/> to call <c>ISession.ResumeAsync</c>
    /// on). The session is loaded from the
    /// <see cref="WorkspaceSessionStore"/> record's stored cwd /
    /// provider / model and the shared <see cref="SessionEnvironment"/>.
    /// Throws <see cref="InvalidOperationException"/> when the id is
    /// unknown.
    /// </summary>
    public static async Task<ISession> ResumeSessionAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_environment is null || _activeSession is null)
            throw new InvalidOperationException(
                "Composition.ResumeSessionAsync called before InitializeAsync");

        var record = WorkspaceSessionStore.FindSession(sessionId)
            ?? throw new InvalidOperationException(
                $"Session '{sessionId}' not found");

        var next = await Session.LoadAsync(
            record.Cwd, _environment, record.ProviderName, record.Model,
            resumeId: sessionId);

        var outgoing = _activeSession.Current;
        _activeSession.Replace(next);
        outgoing?.Dispose();
        return next;
    }
}
