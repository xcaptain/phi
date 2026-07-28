using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Base class for tools whose parameters are a typed record. Handles:
/// <list type="bullet">
/// <item>Schema generation via <see cref="TypedSchema"/></item>
/// <item>Strict deserialization (case-insensitive, rejects unknown fields)</item>
/// <item>Structured error messages for LLM retries when validation fails</item>
/// </list>
/// Subclasses override <see cref="ExecuteTypedAsync"/> to receive validated, typed args.
/// </summary>
public abstract class TypedTool<TArgs> : IHarnessTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    public Tool Tool => new(Name, Description, TypedSchema.For<TArgs>());

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
            var args = rawArguments.Deserialize<TArgs>(StrictJsonOptions)
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

    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}