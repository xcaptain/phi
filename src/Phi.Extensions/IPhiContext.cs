namespace Phi.Extensions;

/// <summary>
/// Read-only projection of the active session, exposed to extensions
/// via <see cref="IPhiContext"/>. Extensions cannot mutate the session
/// through this surface — action methods live on <see cref="IPhiApi"/>.
/// <para>
/// Sprint 0 declares the contract. Sprint 1 wires
/// <c>Phi.Extensions.Host.PhiApi</c> to source these values from
/// <c>Phi.ISession</c> + a per-session <see cref="IPhiUiBridge"/>.
/// </para>
/// </summary>
public interface IPhiContext
{
    /// <summary>Working directory this session is bound to (its workspace).</summary>
    string Cwd { get; }

    /// <summary>Active model name (e.g. <c>"deepseek-v4-flash"</c>).</summary>
    string Model { get; }

    /// <summary>Active provider name (e.g. <c>"deepseek"</c>).</summary>
    string ProviderName { get; }

    /// <summary>Stable session id (matches the persisted <c>SessionRecord.Id</c>).</summary>
    string SessionId { get; }

    /// <summary>The resolved system prompt currently in use by the harness.</summary>
    string SystemPrompt { get; }

    /// <summary>True while a turn / agent run is in flight.</summary>
    bool IsRunning { get; }

    /// <summary>Whether a real UI is attached (TUI / Avalonia). False in headless mode.</summary>
    bool HasUi { get; }

    /// <summary>
    /// Read-only view of the conversation so far (user + assistant + tool
    /// results, in order). Mutating the returned collection has no effect
    /// — submit messages via <see cref="IPhiApi.SubmitUserMessage"/>.
    /// </summary>
    IReadOnlyList<Phi.Agent.IAgentMessage> Transcript { get; }

    /// <summary>
    /// The host UI bridge. Always non-null (a headless environment uses
    /// <see cref="NullPhiUiBridge"/>); see <see cref="HasUi"/> to detect.
    /// </summary>
    IPhiUiBridge Ui { get; }
}
