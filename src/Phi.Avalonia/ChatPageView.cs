using Avalonia.Controls;
using Phi.Avalonia.Components;
using Phi.Chat;
using Phi.Providers;

namespace Phi.Avalonia;

/// <summary>
/// One chat page bound to a single session. Owns the projector that
/// projects the session's chat state, and wires the projector into the
/// <see cref="TranscriptView"/> + <see cref="PromptInputView"/>
/// sub-components. The two-row layout (transcript fills, prompt input
/// docks at the bottom) lives in <see cref="ChatPageLayout"/>; this class
/// only owns the slots, the projector subscription, and disposal.
/// </summary>
public sealed class ChatPageView : IDisposable
{
    private readonly ChatTranscriptProjector _projector;
    private readonly TranscriptView _transcript;
    private readonly PromptInputView _promptInput;
    private readonly ChatPageLayout _layout;

    public ChatPageView(
        ActiveSession active,
        ProviderManager providers,
        ISession session,
        Func<Task<string?>>? pickFolder = null,
        Action<Action>? postToUi = null,
        Action<Action>? dispatchToUi = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(session);

        _projector = new ChatTranscriptProjector(session);

        _transcript = new TranscriptView(dispatchToUi);
        _promptInput = new PromptInputView(
            session,
            active,
            providers,
            _projector,
            pickFolder: pickFolder,
            postToUi: postToUi,
            dispatchToUi: dispatchToUi);

        _transcript.Bind(_projector);

        _layout = new ChatPageLayout
        {
            TranscriptHost = { Content = _transcript.Root },
            PromptInputHost = { Content = _promptInput.Root },
        };
    }

    /// <summary>
    /// The projector that backs this chat page. Used by the shell to wire
    /// extension-runtime UI bridges to the live transcript (Sprint 3).
    /// </summary>
    internal ChatTranscriptProjector Projector => _projector;

    /// <summary>The chat page layout (transcript + prompt input slots).</summary>
    public Control Root => _layout;

    /// <summary>The live prompt input, exposed for integration tests.</summary>
    internal PromptInputView PromptInput => _promptInput;

    /// <summary>The live transcript, exposed for integration tests.</summary>
    internal TranscriptView Transcript => _transcript;

    /// <summary>Disposes the projector, unsubscribing from the session.</summary>
    public void Dispose() => _projector.Dispose();
}
