namespace Phi.Agent;

/// <summary>
/// Marker interface for all agent message types. Lets the provider layer accept a
/// heterogeneous message list while keeping the static type system happy without
/// a Pydantic-style union.
/// <para>
/// <see cref="Role"/> is the tau-aligned discriminator (<c>AgentMessage</c>
/// union with <c>discriminator="role"</c>): a per-type literal value, fixed
/// at compile time on each implementation (get-only, never assignable).
/// </para>
/// </summary>
public interface IAgentMessage
{
    string Role { get; }
}
