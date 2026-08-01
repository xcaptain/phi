using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace PhiAgent;

/// <summary>
/// Base class for tools whose parameters are a typed record. Handles strict
/// deserialization of raw JSON arguments into <typeparamref name="TArgs"/>
/// (case-insensitive, rejects unknown fields) and surfaces structured error
/// messages for LLM retries when validation fails.
/// <para>
/// The JSON type metadata and the tool schema are provided by subclasses:
/// <see cref="ArgsTypeInfo"/> supplies the <see cref="JsonTypeInfo{TArgs}"/>
/// from a source-generated context (required for NativeAOT), and
/// <see cref="Tool"/> is emitted by the <c>PhiSchemaGen</c> source generator.
/// </para>
/// </summary>
public abstract class TypedTool<TArgs> : IHarnessTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    /// <summary>
    /// The tool's schema + metadata. Implemented by the <c>PhiSchemaGen</c>
    /// source generator (each <c>TypedTool&lt;TArgs&gt;</c> subclass must be
    /// <c>partial</c>).
    /// </summary>
    public abstract Tool Tool { get; }

    /// <summary>
    /// Source-generated JSON metadata for <typeparamref name="TArgs"/>.
    /// Subclasses supply this from their own <c>JsonSerializerContext</c>
    /// (e.g. <c>PhiCoding.ToolArgsJsonContext</c>), keeping this base class
    /// free of any application-layer dependency.
    /// </summary>
    protected abstract JsonTypeInfo<TArgs> ArgsTypeInfo { get; }

    async Task<ToolResult> IHarnessTool.ExecuteAsync(string toolName, string toolCallId, JsonNode arguments, CancellationToken ct)
        => await ExecuteAsync(toolCallId, arguments, ct);

    public abstract Task<ToolResult> ExecuteTypedAsync(TArgs args, CancellationToken cancellationToken);

    /// <summary>
    /// Called by the harness with raw JSON arguments from the LLM.
    /// Deserializes strictly, surfacing validation errors as <c>IsError: true</c>
    /// results so the LLM can retry.
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(
        string toolCallId,
        JsonNode rawArguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var args = rawArguments.Deserialize(ArgsTypeInfo)
                ?? throw new JsonException("Arguments deserialized to null");
            return await ExecuteTypedAsync(args, cancellationToken);
        }
        catch (JsonException ex)
        {
            var path = string.IsNullOrEmpty(ex.Path) ? "(root)" : ex.Path;
            return new ToolResult(
                [new TextBlock(
                    $"Tool '{Name}' validation error at {path}: {ex.Message}")],
                IsError: true);
        }
    }
}
