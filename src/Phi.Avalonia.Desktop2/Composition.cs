using Phi.Agent;
using Phi.Providers;

namespace Phi.Avalonia.Desktop2;

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
/// <see cref="SessionEnvironment"/>, and creates an initial session in
/// the user's home directory using the first connected provider's
/// default model.</item>
/// <item>Frontends (<c>ShellViewModel</c>, sidebar, prompt input) read
/// <see cref="ActiveSession"/> and <see cref="Providers"/> to subscribe
/// to session swaps and to enumerate available models.</item>
/// <item><see cref="NewSessionAsync"/> creates a fresh session via
/// <see cref="Session.LoadAsync"/>, swaps it into
/// <see cref="ActiveSession"/>, and returns the new instance. Sidebar
/// <c>New Chat</c> calls into here so the shell sees the swap.</item>
/// </list>
/// </para>
/// <para>
/// UI prototype scope: the initial session is created at startup so the
/// user lands on a usable chat page without clicking through a "start"
/// step. The pre-session state (no <see cref="ISession"/> at all, just
/// a prompt input) is a Sprint-3 follow-up — today's flow is
/// <c>start with default session → user types → submit</c>.
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
    /// <see cref="SessionEnvironment"/>, and loads an initial session
    /// in the user's home directory using the first connected
    /// provider's default model. Idempotent — calling twice returns
    /// without rebuilding.
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

        // Find the first connected provider to use for the initial
        // session. If none are connected, the call below will throw
        // InvalidOperationException via ProviderResolver — the UI
        // prototype surfaces this as a startup error.
        var firstConnected = ProviderCatalog.All
            .Where(_providers.HasApiKey)
            .Cast<ProviderCatalogEntry?>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No provider has an API key configured. " +
                "Open the Providers page and save a key before starting a chat.");

        _environment = SessionEnvironment.Default(_providers);

        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(cwd)) cwd = Environment.CurrentDirectory;

        var initial = await Session.LoadAsync(
            cwd,
            _environment,
            firstConnected.Name,
            firstConnected.DefaultModel);

        _activeSession = new ActiveSession(initial);
    }

    /// <summary>
    /// Creates a fresh session in <paramref name="cwd"/> using
    /// <paramref name="provider"/> + <paramref name="model"/>, swaps it
    /// into <see cref="ActiveSession"/>, disposes the outgoing session,
    /// and returns the new instance. Sidebar <c>New Chat</c> calls into
    /// here.
    /// <para>
    /// Always uses <see cref="Session.LoadAsync"/> directly (rather than
    /// <see cref="ISession.NewSessionAsync"/>) so the new session takes
    /// the chosen provider/model — <c>NewSessionAsync</c> on the live
    /// session inherits the outgoing session's provider/model which is
    /// the opposite of what <c>New Chat</c> wants.
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
}