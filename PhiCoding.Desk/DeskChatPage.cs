using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using PhiCoding.Chat;
using PhiCoding.Desk.Components;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Desk;

/// <summary>
/// One chat page bound to a single session. Owns the projector + the
/// header / transcript / input / status bar components. Disposed when the
/// page is torn down (navigation) so subscriptions stop firing.
/// </summary>
internal sealed class DeskChatPage : IDisposable
{
    private readonly ISession _session;
    private readonly ChatTranscriptProjector _projector;
    private readonly StatusBarView _statusBar;
    private readonly TranscriptView _transcript;
    private readonly PromptInputView _promptInput;

    public DeskChatPage(ISessionNavigator navigator, ProviderManager providers, ISession session)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _projector = new ChatTranscriptProjector(session);

        _statusBar = new StatusBarView();
        _transcript = new TranscriptView();
        _promptInput = new PromptInputView(session, navigator, providers, _projector);

        _transcript.Bind(_projector);
        _statusBar.BindStatusBar(session);
        _promptInput.Build();

        // The transcript is the LAST child so it fills the remaining space
        // (DockPanel.LastChildFill). Header is docked on top; the prompt
        // input and status bar dock at the bottom.
        Root = new DockPanel()
            .LastChildFill()
            .Children(
                BuildHeader().DockTop(),
                _promptInput.Root.DockBottom(),
                _statusBar.Root.DockBottom(),
                _transcript.Root);
    }

    public FrameworkElement Root { get; }

    /// <summary>The transcript's scroll root, exposed for layout tests.</summary>
    internal FrameworkElement TranscriptRoot => _transcript.Root;

    /// <summary>The prompt input root, exposed for layout tests.</summary>
    internal FrameworkElement PromptInputRoot => _promptInput.Root;

    /// <summary>The live prompt input, exposed for integration tests.</summary>
    internal PromptInputView PromptInput => _promptInput;

    /// <summary>The live transcript, exposed for integration tests.</summary>
    internal TranscriptView Transcript => _transcript;

    /// <summary>Disposes the projector, unsubscribing from the session.</summary>
    public void Dispose() => _projector.Dispose();

    private FrameworkElement BuildHeader() => new ChatHeaderView(_session).Root;
}