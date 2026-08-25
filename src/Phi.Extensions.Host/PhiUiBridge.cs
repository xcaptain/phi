namespace Phi.Extensions.Host;

/// <summary>
/// Generic <see cref="IPhiUiBridge"/> implementation that forwards every
/// call to an injected <see cref="IUiSink"/>. The composition root builds
/// one with a UI-specific sink (TUI uses XenoAtom dialogs + PhiStatusBar,
/// Avalonia uses its dialog API + desk log + status bar) and gives it to
/// <see cref="ExtensionRuntime"/> via <c>new ExtensionRuntime(session, bridge)</c>.
/// <para>
/// <c>HasUi</c> comes from the sink so headless contexts (CI / automation)
/// can return <c>false</c> from a no-op <see cref="IUiSink"/> and the
/// extension's <c>api.Context.Ui.HasUi</c> short-circuit path triggers.
/// Dialog methods forward to the sink regardless — the sink itself is
/// responsible for returning no-op defaults when <c>HasUi == false</c>
/// (the canonical <see cref="NullUiSink"/> does this).
/// </para>
/// </summary>
public sealed class PhiUiBridge : IPhiUiBridge
{
    private readonly Func<IUiSink> _sinkAccessor;

    /// <summary>
    /// Build a bridge that forwards every call to the sink returned by
    /// <paramref name="sinkAccessor"/>. Use the accessor overload when the
    /// UI element the sink wraps can be swapped at runtime — e.g. the TUI
    /// rebuilds the chat page (and its transcript / status bar) on every
    /// <c>ISession.NewSessionAsync</c>; the bridge resolves the current
    /// sink lazily so extensions calling <see cref="Notify"/> /
    /// <see cref="FlashError"/> after a navigation still hit the live UI.
    /// </summary>
    public PhiUiBridge(Func<IUiSink> sinkAccessor)
    {
        ArgumentNullException.ThrowIfNull(sinkAccessor);
        _sinkAccessor = sinkAccessor;
    }

    /// <summary>Convenience overload that wraps a single sink instance.</summary>
    public PhiUiBridge(IUiSink sink) : this(() => sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
    }

    /// <inheritdoc />
    public bool HasUi => Resolve().HasUi;

    /// <inheritdoc />
    public void Notify(string message, NotifyLevel level = NotifyLevel.Info)
        => Resolve().Notify(message, level);

    /// <inheritdoc />
    public void NotifyStatus(string message) => Resolve().NotifyStatus(message);

    /// <inheritdoc />
    public void FlashError(string message, bool persistent)
        => Resolve().FlashError(message, persistent);

    /// <inheritdoc />
    public void SubmitTranscriptLine(TranscriptLine line)
        => Resolve().SubmitTranscriptLine(line);

    /// <summary>
    /// Internal forwarding for <c>SubmitCustomMessage</c> rendering — not part
    /// of the frozen <see cref="IPhiUiBridge"/> surface; used by
    /// <see cref="ExtensionRuntime.SubmitCustomMessage"/> to push the custom
    /// message line into the live projector.
    /// </summary>
    internal void SubmitCustomMessageLine(
        string customType,
        string content,
        IReadOnlyDictionary<string, object?>? details)
        => Resolve().SubmitCustomMessageLine(customType, content, details);

    /// <inheritdoc />
    public Task<string?> SelectAsync(string title, IReadOnlyList<string> options, TimeSpan? timeout = null)
        => Resolve().ShowSelectAsync(title, options, timeout);

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message, TimeSpan? timeout = null)
        => Resolve().ShowConfirmAsync(title, message, timeout);

    /// <inheritdoc />
    public Task<string?> InputAsync(string title, string placeholder = "", TimeSpan? timeout = null)
        => Resolve().ShowInputAsync(title, placeholder, timeout);

    private IUiSink Resolve()
    {
        var sink = _sinkAccessor();
        if (sink is null)
            throw new InvalidOperationException(
                "IPhiUiBridge has no sink bound. The composition root must provide a sink " +
                "via PhiUiBridge(Func<IUiSink>) before the extension calls bridge methods.");
        return sink;
    }
}
