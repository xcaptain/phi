using Phi.Agent;
using Phi.Prompts;

namespace Phi;

/// <summary>
/// Session contract for a single conversation. Frontends bind to state
/// changes via <see cref="StateChanged"/> and <see cref="HarnessEvent"/>, and
/// dispatch user actions through the action methods.
/// <para>
/// Session <em>switching</em> is not part of this contract — navigating
/// between sessions (new / resume) is owned by
/// <see cref="Sessions.ISessionNavigator"/>, which disposes the outgoing
/// session.
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

    /// <summary>Working directory this session is bound to (its workspace).</summary>
    string Cwd { get; }

    /// <summary>
    /// Skills available to this session (project + user level), for
    /// autocompleting <c>/skill:NAME</c> and surfacing in the prompt.
    /// </summary>
    IReadOnlyList<SkillDescriptor> Skills { get; }

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
}
