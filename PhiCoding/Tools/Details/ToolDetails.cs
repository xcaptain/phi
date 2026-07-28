using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhiCoding.Tools.Details;

/// <summary>
/// Round-trips typed <see cref="ToolResult.Details"/> records through
/// <see cref="JsonNode"/> using camelCase property names. Tools call
/// <see cref="Node{T}"/> to package details; renderers call <see cref="Read{T}"/>
/// to recover the typed shape.
/// </summary>
public static class ToolDetails
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static JsonNode? Node<T>(T details) where T : class
        => JsonSerializer.SerializeToNode(details, JsonOpts);

    public static T? Read<T>(JsonNode? node) where T : class
        => node?.Deserialize<T>(JsonOpts);
}