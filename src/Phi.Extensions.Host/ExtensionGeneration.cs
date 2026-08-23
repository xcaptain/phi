namespace Phi.Extensions.Host;

/// <summary>
/// Generation token for one extension's lifetime. <see cref="Invalidate"/>
/// is called once per <c>/reload</c>; afterwards, the
/// <see cref="IPhiApi"/> instance bound to this generation throws
/// <see cref="ExtensionGenerationException"/> on every action method
/// (catches'd failures are written to the audit log; UI-visible failures
/// fire <see cref="IPhiUiBridge.FlashError"/>).
/// <para>
/// This is the runtime-side half of <c>docs/extensions.md §7.1</c>. The
/// extension-side counterpart is the captured <c>IPhiApi</c> reference:
/// when an extension does
/// <c>var api = ...; someBgTask.ContinueWith(_ => api.SubmitUserMessage(...))</c>,
/// the closure captures the OLD generation's api; on <c>/reload</c> the
/// captured api is invalidated and the background call throws instead of
/// silently writing into the new session's transcript.
/// </para>
/// </summary>
internal sealed class ExtensionGeneration
{
    private volatile bool _alive = true;
    private string? _staleMessage;

    public bool IsAlive => _alive;

    /// <summary>The extension name (set once at construction for diagnostics).</summary>
    public string ExtensionName { get; }

    /// <summary>
    /// Sequential id — increments on every <see cref="Invalidate"/>, useful
    /// for log correlation ("this hook fired against generation 3 of
    /// hello-tool, which was stale-asserted").
    /// </summary>
    public int Version { get; private set; }

    public ExtensionGeneration(string extensionName)
    {
        ExtensionName = extensionName ?? throw new ArgumentNullException(nameof(extensionName));
    }

    /// <summary>
    /// Mark this generation as stale. First call wins for the message
    /// (matches tau's "first error message wins" behavior so reloaded
    /// extensions see a stable reason rather than cascading errors).
    /// </summary>
    public void Invalidate(string? reason = null)
    {
        if (!_alive) return;       // already stale; don't double-fire hooks
        _alive = false;
        Version++;
        _staleMessage ??= reason;
    }

    /// <summary>
    /// Throw <see cref="ExtensionGenerationException"/> if this generation
    /// has been invalidated. Called at the top of every <see cref="Phi.Extensions.IPhiApi"/>
    /// action method on the host side.
    /// </summary>
    public void AssertAlive()
    {
        if (!_alive)
        {
            throw new ExtensionGenerationException(
                ExtensionName,
                Version,
                _staleMessage ?? $"extension '{ExtensionName}' generation {Version} stale after /reload");
        }
    }
}

/// <summary>
/// Thrown by <see cref="ExtensionGeneration.AssertAlive"/>. The PhiApi
/// implementation catches this and writes a single audit-log line
/// (first-error-wins), then surfaces a status-bar flash. Extensions
/// should NOT catch this in user code — it means the extension was
/// reloaded mid-callback and the captured api is no longer valid.
/// </summary>
public sealed class ExtensionGenerationException : Exception
{
    public string ExtensionName { get; }
    public int StaleGenerationVersion { get; }

    public ExtensionGenerationException(string extensionName, int version, string message)
        : base(message)
    {
        ExtensionName = extensionName;
        StaleGenerationVersion = version;
    }
}
