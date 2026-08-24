using Phi.Chat;
using Phi.Extensions.Host;
using Phi.Providers;
using Phi.Tui.Components;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
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
        : this(initialSession, providers, null, null, null)
    {
    }

    public PhiTuiApp(ISession initialSession, ProviderManager providers, TuiDialogShower? dialogShower)
        : this(initialSession, providers, dialogShower, null, null)
    {
    }

    public PhiTuiApp(
        ISession initialSession,
        ProviderManager providers,
        TuiDialogShower? dialogShower,
        Action<IUiSink>? onSinkBuilt)
        : this(initialSession, providers, dialogShower, onSinkBuilt, null)
    {
    }

    public PhiTuiApp(
        ISession initialSession,
        ProviderManager providers,
        TuiDialogShower? dialogShower,
        Action<IUiSink>? onSinkBuilt,
        Func<IExtensionRenderers?>? renderersAccessor)
    {
        ArgumentNullException.ThrowIfNull(initialSession);
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
        // The dialog shower is optional in tests; in production
        // Program.cs wires the real XenoAtom-backed implementation.
        _dialogShower = dialogShower ?? new TuiDialogShower(() => null!);
        _onSinkBuilt = onSinkBuilt ?? (_ => { });
        _renderersAccessor = renderersAccessor;
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
        Terminal.Run(toastHost, () => TerminalLoopResult.Continue);
    }

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

        var transcript = new ChatTranscript();
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

        var input = new PromptInput(session, _providers, transcript);
        input.SessionReplaced += OnSessionReplaced;
        input.Build();

        // Sprint 4: hand the extension renderers to the projector so custom
        // transcript lines / tool cards / descriptors route to the
        // registered renderers instead of the static fallbacks.
        transcript.Bind(session, _renderersAccessor?.Invoke());
        StatusBarBinder.Bind(statusBar, transcript, session);

        // Empty session? Show a slogan in the transient region; the first
        // submitted user prompt replaces it via the input itself.
        if (session.State.Messages.Count == 0)
            transcript.ShowTransient("Phi — a minimal and portable coding agent");

        var header = ChatHeader.Build(session);

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
}
