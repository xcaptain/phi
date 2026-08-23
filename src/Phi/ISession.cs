using Phi.Agent;
using Phi.Prompts;

namespace Phi;

/// <summary>
/// Session contract for a single conversation. Frontends bind to state
/// changes via <see cref="StateChanged"/> and <see cref="HarnessEvent"/>, and
/// dispatch user actions through the action methods.
/// <para>
/// Session <em>switching</em> is part of this contract: <see cref="NewSessionAsync"/>
/// creates a fresh session in the same (or a chosen) workspace, and
/// <see cref="ResumeAsync"/> opens an indexed session by id. Both return the
/// new session and dispose this one before returning — frontends just
/// reassign their reactive binding (<c>State&lt;ISession&gt;.Value = next</c>
/// in the TUI, or an equivalent event in the Avalonia shell). No separate
/// "navigator" entity is involved.
/// </para>
/// <para>
/// Implementing <see cref="IDisposable"/> signals the session owns scoped
/// resources (notably the in-flight run's <see cref="CancellationTokenSource"/>
/// and the provider's HTTP transport). Dispose cancels any active run,
/// awaits it briefly, and releases the cancellation source.
/// </para>
/// </summary>
public interface ISession : IDisposable
{
    event Action<SessionState>? StateChanged;
    event Action<HarnessEvent>? HarnessEvent;
    SessionState State { get; }

    /// <summary>Stable session id (matches the persisted <see cref="SessionRecord.Id"/>).</summary>
    string Id { get; }

    /// <summary>Working directory this session is bound to (its workspace).</summary>
    string Cwd { get; }

    /// <summary>
    /// The resolved system prompt currently in use by the harness (after
    /// tool-contribution rendering). Exposed for the extension
    /// <c>IPhiContext.SystemPrompt</c> view; tests and UI can read it for
    /// diagnostics / display. Empty string if the session hasn't been
    /// bound yet (no <c>ApplyRuntime</c> call).
    /// </summary>
    string SystemPrompt { get; }

    /// <summary>
    /// Whether the host that constructed this session has a real UI
    /// attached (TUI / Avalonia). <c>false</c> means headless mode (CI,
    /// automation, unit tests) — extensions should expect dialog calls
    /// to return no-op defaults via <see cref="IPhiUiBridge"/>'s
    /// <c>HasUi = false</c> path. Set by the composition root after
    /// <see cref="Session.LoadAsync"/>; mutable so tests can flip it.
    /// </summary>
    bool HasUi { get; set; }

    /// <summary>
    /// Skills available to this session (project + user level), for
    /// autocompleting <c>/skill:NAME</c> and surfacing in the prompt.
    /// </summary>
    IReadOnlyList<SkillDescriptor> Skills { get; }

    /// <summary>
    /// Names of the providers available in the catalog, in display order.
    /// Used by the <c>/connect</c> / <c>/models</c> dialogs and the
    /// desktop model picker.
    /// </summary>
    IReadOnlyList<string> AvailableProviders { get; }

    void SubmitPrompt(string text);
    void Cancel();
    void EnqueueSteering(UserMessage message);
    void EnqueueFollowUp(UserMessage message);
    void RenameSession(string? title);

    /// <summary>
    /// Loads a skill's <c>SKILL.md</c> into the conversation and starts a run
    /// so the model acts on it immediately (bare <c>/skill:NAME</c> runs the
    /// skill; a trailing <c>prompt</c> is fused into the same user message).
    /// Returns the submitted message content so frontends can render it.
    /// </summary>
    Task<string> LoadSkillAsync(string name, string? prompt = null);

    /// <summary>
    /// Switches the active model within the current provider. Applies to the
    /// next run only; provider resources are untouched.
    /// </summary>
    void SwitchModel(string model);

    /// <summary>
    /// Switches to a new provider instance (the session takes ownership and
    /// disposes the previous provider) with its <paramref name="model"/>.
    /// Applies to the next run only.
    /// </summary>
    void SwitchProvider(IPhiProvider provider, string providerName, string model);

    /// <summary>
    /// Creates a fresh session in <paramref name="cwd"/> (or the current
    /// session's cwd when null) inheriting the current session's provider
    /// and model. The new session is returned; this session is disposed
    /// before returning.
    /// <para>
    /// Frontend binding pattern (TUI):
    /// <c>_currentSession.Value = await _currentSession.Value.NewSessionAsync(cwd);</c>
    /// </para>
    /// </summary>
    Task<ISession> NewSessionAsync(string? cwd = null);

    /// <summary>
    /// Resumes the indexed session identified by <paramref name="sessionId"/>.
    /// Resolves the session's own cwd from its record so cross-workspace
    /// resume works (the desktop shell lists sessions across every project;
    /// the record's cwd is the source of truth). The new session is
    /// returned; this session is disposed before returning. Throws
    /// <see cref="InvalidOperationException"/> when the id is unknown.
    /// </summary>
    Task<ISession> ResumeAsync(string sessionId);

    /// <summary>
    /// Indexed sessions of this session's project, last touched within
    /// <paramref name="days"/> days, newest first. Backed by the same
    /// <see cref="SessionManager"/> the session itself uses, so a freshly
    /// persisted session appears on the next call.
    /// </summary>
    IReadOnlyList<SessionRecord> ListRecent(int days = 7);
}
