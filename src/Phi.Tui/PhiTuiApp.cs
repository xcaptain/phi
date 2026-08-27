using System.Globalization;
using Phi.Chat;
using Phi.Extensions;
using Phi.Extensions.Host;
using Phi.Providers;
using Phi.Slash;
using Phi.Tui.Components;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Hosting;
using XenoAtom.Terminal.UI.Styling;

namespace Phi.Tui;

/// <summary>
/// TUI shell. The chat screen is built from components in
/// <see cref="Components"/> (header, transcript, status bar, input);
/// <c>/new</c> and <c>/sessions</c> are answered by the
/// <see cref="ISession"/> itself (<see cref="ISession.NewSessionAsync"/> /
/// <see cref="ISession.ResumeAsync"/>) — the new session is returned, the
/// old one disposes itself, and the page host rebuilds on the next render
/// (its <c>State&lt;ISession&gt;</c> read was the only reactive dependency).
/// <see cref="PromptInput"/> raises a navigation event when it swaps the
/// session; the shell listens and flips the State so the
/// <c>ComputedVisual</c> re-runs the page builder. The layout skeleton
/// (header on top, transcript in the middle, editor + suggestion strip +
/// status bar at the bottom) is fixed.
/// </summary>
public sealed class PhiTuiApp
{
    private readonly ProviderManager _providers;
    private readonly State<ISession> _currentSession;
    private readonly TuiDialogShower _dialogShower;
    private readonly Action<IUiSink> _onSinkBuilt;
    private readonly Func<IExtensionRenderers?>? _renderersAccessor;
    private readonly Func<ISlashCommandRegistry?>? _commandsAccessor;
    private readonly Func<IPhiContext?>? _contextAccessor;

    /// <summary>
    /// The thread marshaller that every chat-page component reaches for
    /// when it needs to touch a XenoAtom visual from a non-UI thread.
    /// Built in <see cref="Run"/> once the <see cref="TerminalApp"/>
    /// exists; stays <see cref="TuiUiThread.None"/> for tests that never
    /// spin up the terminal loop.
    /// </summary>
    private TuiUiThread _uiThread = TuiUiThread.None;

    /// <summary>
    /// Fired every time a chat page is built (initially + on every
    /// session navigation). The composition root uses this to wire the
    /// fresh sink into the extension runtime's bridge so calls land on
    /// the new page's transcript + status bar instead of the disposed
    /// outgoing ones.
    /// </summary>
    public event Action<IUiSink>? SinkBuilt;

    /// <summary>
    /// The most recently built sink. Exposed for tests; production code
    /// reads <see cref="SinkBuilt"/> instead so it never observes a
    /// half-built page.
    /// </summary>
    public IUiSink CurrentSink { get; private set; } = new NullUiSink();

    public PhiTuiApp(ISession initialSession, ProviderManager providers)
        : this(initialSession, providers, null, null, null, null, null)
    {
    }

    public PhiTuiApp(ISession initialSession, ProviderManager providers, TuiDialogShower? dialogShower)
        : this(initialSession, providers, dialogShower, null, null, null, null)
    {
    }

    public PhiTuiApp(
        ISession initialSession,
        ProviderManager providers,
        TuiDialogShower? dialogShower,
        Action<IUiSink>? onSinkBuilt)
        : this(initialSession, providers, dialogShower, onSinkBuilt, null, null, null)
    {
    }

    public PhiTuiApp(
        ISession initialSession,
        ProviderManager providers,
        TuiDialogShower? dialogShower,
        Action<IUiSink>? onSinkBuilt,
        Func<IExtensionRenderers?>? renderersAccessor)
        : this(initialSession, providers, dialogShower, onSinkBuilt, renderersAccessor, null, null)
    {
    }

    public PhiTuiApp(
        ISession initialSession,
        ProviderManager providers,
        TuiDialogShower? dialogShower,
        Action<IUiSink>? onSinkBuilt,
        Func<IExtensionRenderers?>? renderersAccessor,
        Func<ISlashCommandRegistry?>? commandsAccessor)
        : this(initialSession, providers, dialogShower, onSinkBuilt, renderersAccessor, commandsAccessor, null)
    {
    }

    public PhiTuiApp(
        ISession initialSession,
        ProviderManager providers,
        TuiDialogShower? dialogShower,
        Action<IUiSink>? onSinkBuilt,
        Func<IExtensionRenderers?>? renderersAccessor,
        Func<ISlashCommandRegistry?>? commandsAccessor,
        Func<IPhiContext?>? contextAccessor)
    {
        ArgumentNullException.ThrowIfNull(initialSession);
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
        // The dialog shower is optional in tests; in production
        // Program.cs wires the real XenoAtom-backed implementation.
        // The placeholder marshaller is the no-op TuiUiThread.None;
        // Run() replaces it with the live TerminalApp-bound one before
        // any user input can land.
        _dialogShower = dialogShower ?? new TuiDialogShower(TuiUiThread.None);
        _onSinkBuilt = onSinkBuilt ?? (_ => { });
        _renderersAccessor = renderersAccessor;
        _commandsAccessor = commandsAccessor;
        _contextAccessor = contextAccessor;
        _currentSession = new State<ISession>(initialSession);
    }

    /// <summary>
    /// The page host: a <c>ComputedVisual</c> whose builder reads the
    /// current-session <see cref="State{T}"/> (a tracked read — the library
    /// marks the host dirty and re-invokes the builder when navigation
    /// changes it) and returns the chat page for that session.
    /// </summary>
    public Visual BuildRoot()
        => new ComputedVisual(BuildCurrentPage)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };

    public void Run()
    {
        using var terminal = Terminal.Open();
        var root = BuildRoot();
        // ToastHost overlays transient notifications (used by
        // SelectionCopyHost to confirm auto-copies); SelectionCopyHost wires
        // mouse drag-select / double-click → clipboard auto-copy.
        var toastHost = new ToastHost(new SelectionCopyHost(root));
        // Construct the TerminalApp directly (rather than going through
        // Terminal.Run) so we can hold a reference to it — every TUI
        // component (ChatTranscript, ChatHeader, PhiStatusBar, PromptInput
        // dialogs, TuiDialogShower) needs the app's Dispatcher to marshal
        // background-thread UI mutations back to the UI thread. The
        // options here mirror the fullscreen defaults Terminal.Run
        // wires internally; tweaking them requires updating the same
        // knobs the bundled TuiDialogShowerIntegrationTests assert on.
        var app = new TerminalApp(toastHost, terminal.Instance, new TerminalAppOptions
        {
            HostKind = TerminalHostKind.Fullscreen,
            RawMode = TerminalRawModeKind.CBreak,
            DisableInputEcho = true,
            EnableMouse = true,
            InitialFocusMode = InitialFocusMode.FirstFocusable,
            Culture = CultureInfo.InvariantCulture,
            LoopMode = TerminalLoopMode.Auto,
            UpdateWaitDuration = TimeSpan.FromMilliseconds(1),
            WideRuneResolver = TerminalWideRuneResolvers.Default,
        });
        // The dispatcher is now reachable from every component through
        // _uiThread; we publish it on the shell so PhiTuiApp.BuildCurrentPage
        // can hand the same marshaller to the page builder, the dialog
        // shower, and the prompt input. The dialog shower was
        // constructed earlier with a placeholder; replace it now that the
        // TerminalApp exists.
        _uiThread = new TuiUiThread(app);
        _dialogShower.SetUiThread(_uiThread);
        // Run synchronously on the calling thread (the program's main
        // thread); the dispatcher bound to `app` becomes the UI thread.
        app.Run();
    }

    /// <summary>
    /// Holds a reference to the live <see cref="TerminalApp"/> so the
    /// <see cref="TuiDialogShower"/> and the per-page marshaller can
    /// resolve it. Set once at the start of <see cref="Run"/> and stays
    /// stable for the shell's lifetime. The marshal helper
    /// (<see cref="TuiUiThread"/>) holds the actual reference; this
    /// field is just a stable handle to the live dispatcher.
    /// </summary>

    /// <summary>
    /// Builds the chat page for the current session. The session is read
    /// through <see cref="State{T}"/> so the computed visual rebuilds on
    /// navigation; closures (editor accepted, status-bar events) capture the
    /// session bound at build time, so the outgoing session is no longer
    /// referenced after the swap.
    /// </summary>
    private DockLayout BuildCurrentPage()
    {
        var session = _currentSession.Value;

        // _uiThread was wired in Run() before the app loop started; for
        // tests that never call Run() it stays at the no-op default.
        var transcript = new ChatTranscript(_uiThread);
        var statusBar = new PhiStatusBar(session.State.Model);

        // Sprint 3: the bridge that backs this session's extension runtime
        // resolves its sink lazily; rebind on every page build so
        // extensions hitting the bridge after a /new or /sessions land on
        // the new page's transcript + status bar (not the disposed
        // outgoing ones).
        var sink = new TuiUiSink(transcript, statusBar, _dialogShower);
        CurrentSink = sink;
        SinkBuilt?.Invoke(sink);
        _onSinkBuilt(sink);

        // Build a single dispatcher closure so the conditional expression has
        // a single nullable reference type (null when no registry is wired).
        Func<string, string, bool, string?>? dispatcher = null;
        if (_commandsAccessor is { } registryAccessor)
        {
            // The handler receives the live session context (read from
            // Session.SystemPrompt / Cwd / etc.); the accessor is null in
            // hosts without an extension runtime, in which case dispatch is
            // disabled anyway.
            ISlashCommandRegistry? registry = registryAccessor();
            IPhiContext? context = _contextAccessor?.Invoke();
            if (registry is not null && context is not null)
            {
                dispatcher = (name, args, _) =>
                    registry.TryDispatch(name, args, context, out var msg) ? msg : null;
            }
        }

        var input = new PromptInput(
            session,
            _providers,
            transcript,
            commands: ResolveAllCommands(_commandsAccessor?.Invoke()),
            dispatcher: dispatcher,
            uiThread: _uiThread);
        input.SessionReplaced += OnSessionReplaced;
        input.Build();

        // Sprint 4: hand the extension renderers to the projector so custom
        // transcript lines / tool cards / descriptors route to the
        // registered renderers instead of the static fallbacks. Pass the
        // UI marshaller explicitly so the bind path upgrades the
        // transcript's marshaller from the constructor default (None)
        // to the live dispatcher.
        transcript.Bind(session, _renderersAccessor?.Invoke(), _uiThread);
        StatusBarBinder.Bind(statusBar, transcript, session, _uiThread);

        // Empty session? Show a slogan in the transient region; the first
        // submitted user prompt replaces it via the input itself.
        if (session.State.Messages.Count == 0)
            transcript.ShowTransient("Phi — a minimal and portable coding agent");

        var header = ChatHeader.Build(session, _uiThread);

        var root = new DockLayout()
            .Top(header)
            .Content(transcript.Visual)
            .Bottom(new VStack(input.Editor.Scrollable(), input.SuggestionStrip.Visual, statusBar.Visual).Spacing(0)
                .Margin(new Thickness(0, 1, 0, 0)))
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        root.SetStyle(Theme.Key, Theme.Default);
        return root;
    }

    /// <summary>
    /// Flips the current-session <see cref="State{T}"/> when the input
    /// answers a <c>/new</c> or <c>/sessions</c> slash command. The
    /// <c>ComputedVisual</c> reads the State at build time, so assigning a
    /// new value invalidates the page and the next render rebuilds it
    /// against the replacement session.
    /// </summary>
    private void OnSessionReplaced(ISession next)
    {
        _currentSession.Value = next;
    }

    /// <summary>
    /// Merges built-in commands (<see cref="SlashCommandCatalog.All"/>) with
    /// whatever the supplied registry exposes. Returns
    /// <see cref="SlashCommandCatalog.All"/> unchanged when no registry is
    /// available (headless / no-extensions case).
    /// </summary>
    private static IReadOnlyList<SlashCommandDef> ResolveAllCommands(
        ISlashCommandRegistry? registry)
    {
        if (registry is null) return SlashCommandCatalog.All;
        var merged = new List<SlashCommandDef>(
            SlashCommandCatalog.All.Count + /* rough */ 16);
        merged.AddRange(SlashCommandCatalog.All);
        merged.AddRange(registry.AllCommands);
        return merged;
    }
}
