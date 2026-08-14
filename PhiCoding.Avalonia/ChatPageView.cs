using Avalonia.Controls;
using PhiCoding.Avalonia.Components;
using PhiCoding.Chat;
using PhiCoding.Providers;
using PhiCoding.Sessions;

namespace PhiCoding.Avalonia;

/// <summary>
/// One chat page bound to a single session. Owns the projector + the
/// header / transcript / prompt input components. Disposed when the page
/// is torn down (navigation) so subscriptions stop firing.
/// </summary>
public sealed class ChatPageView : IDisposable
{
    private readonly ISession _session;
    private readonly ChatTranscriptProjector _projector;
    private readonly TranscriptView _transcript;
    private readonly PromptInputView _promptInput;

    public ChatPageView(
        ISessionNavigator navigator,
        ProviderManager providers,
        ISession session,
        Func<Task<string?>>? pickFolder = null,
        Action<Action>? postToUi = null,
        Action<Action>? dispatchToUi = null)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _projector = new ChatTranscriptProjector(session);

        _transcript = new TranscriptView(dispatchToUi);
        _promptInput = new PromptInputView(
            session,
            navigator,
            providers,
            _projector,
            pickFolder: pickFolder,
            postToUi: postToUi,
            dispatchToUi: dispatchToUi);

        _transcript.Bind(_projector);
        var input = _promptInput.Build();

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        var header = new ChatHeaderView(_session).Root;
        Grid.SetRow(header, 0);
        Grid.SetRow(_transcript.Root, 1);
        Grid.SetRow(input, 2);
        grid.Children.Add(header);
        grid.Children.Add(_transcript.Root);
        grid.Children.Add(input);
        Root = grid;
    }

    public Control Root { get; }

    /// <summary>The live prompt input, exposed for integration tests.</summary>
    internal PromptInputView PromptInput => _promptInput;

    /// <summary>The live transcript, exposed for integration tests.</summary>
    internal TranscriptView Transcript => _transcript;

    /// <summary>Disposes the projector, unsubscribing from the session.</summary>
    public void Dispose() => _projector.Dispose();
}
