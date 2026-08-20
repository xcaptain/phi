using Phi.Providers;
using Phi.Sessions;
using Phi.Tui.Components;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Phi.Tui;

/// <summary>
/// TUI shell over <see cref="ISessionNavigator"/>. The chat screen is built
/// from components in <see cref="Components"/> (header, transcript, status
/// bar, input); navigating to a new session tears the page down and mounts
/// a fresh one against the new session so every closure captures exactly
/// that session. The layout skeleton (header on top, transcript in the
/// middle, editor + suggestion strip + status bar at the bottom) is fixed.
/// <para>
/// Session teardown is owned by the navigator, not the TUI: the navigator
/// cancels + awaits + disposes the outgoing session before the TUI rebuilds.
/// </para>
/// </summary>
public sealed class PhiTuiApp
{
    private readonly ISessionNavigator _navigator;
    private readonly ProviderManager _providers;
    private readonly State<ISession> _currentSession;

    public PhiTuiApp(ISessionNavigator navigator, ProviderManager providers)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        _navigator = navigator;
        _providers = providers;
        _currentSession = new State<ISession>(navigator.Current);

        // The navigator has already swapped the session; flip the state and
        // the page host rebuilds the chat page on the next render.
        _navigator.SessionChanged += () => _currentSession.Value = _navigator.Current;
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
        // Workaround for a XenoAtom.Terminal.UI 3.8.1 ToastHost bug: without
        // it, a toast shown after the previous one fully expired is dismissed
        // instantly. Remove when the upstream fix ships and NuGet is bumped.
        ToastHostSentinel.Install(toastHost);
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

        var input = new PromptInput(session, _navigator, _providers, transcript);
        input.Build();

        transcript.Bind(session);
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
}
