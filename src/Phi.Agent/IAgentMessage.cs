namespace Phi.Agent;

/// <summary>
/// Marker interface for all agent message types. Lets the provider layer accept a
/// heterogeneous message list while keeping the static type system happy without
/// a Pydantic-style union.
/// </summary>
public interface IAgentMessage;
