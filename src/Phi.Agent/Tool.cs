using System.Text.Json.Nodes;

namespace Phi.Agent;

/// <summary>
/// A tool exposed to the agent loop: carries its schema
/// (<see cref="Name"/>, <see cref="Description"/>, <see cref="Parameters"/>)
/// and the logic that executes it (<see cref="ExecuteAsync"/>).
/// <para>
/// Concrete tools either subclass <see cref="TypedTool{TArgs}"/> (which
/// handles JSON-argument deserialization and delegates to a typed
/// <c>ExecuteTypedAsync</c>) or implement this class directly. The schema
/// (<see cref="Parameters"/>) is emitted by the <c>Phi.SchemaGen</c> source
/// generator for <c>TypedTool</c> subclasses.
/// </para>
/// </summary>
public abstract class Tool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract JsonObject Parameters { get; }

    /// <summary>
    /// Executes the tool with raw JSON arguments from the LLM. Implementors
    /// should return <c>IsError: true</c> for expected failures so the model
    /// can retry.
    /// </summary>
    public abstract Task<ToolResult> ExecuteAsync(
        string toolName,
        string toolCallId,
        JsonObject arguments,
        CancellationToken cancellationToken);
}
