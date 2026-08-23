using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Phi.Agent;

namespace Phi.Extensions.CodingPack.Tools.Details;

/// <summary>
/// Round-trips typed <see cref="ToolResult.Details"/> records through
/// <see cref="JsonNode"/>. Tools call <see cref="Node{T}"/> to package
/// details; renderers call <see cref="Read{T}"/> to recover the typed
/// shape. Type metadata comes from the source-generated
/// <see cref="CodingPackJsonContext"/> via <c>GetTypeInfo(Type)</c>, so the
/// calls stay NativeAOT-safe without threading <see cref="JsonTypeInfo{T}"/>
/// through every call site.
/// <para>
/// Sprint 2.5: the tools moved out of the Phi core into CodingPack, so this
/// reads from <see cref="CodingPackJsonContext"/> (this assembly's own AOT
/// context), not the core's <c>PhiJsonContext</c>.
/// </para>
/// </summary>
public static class ToolDetails
{
    public static JsonNode? Node<T>(T details) where T : class
    {
        var typeInfo = (JsonTypeInfo<T>)CodingPackJsonContext.Default.GetTypeInfo(typeof(T))!;
        return JsonSerializer.SerializeToNode(details, typeInfo);
    }

    public static T? Read<T>(JsonNode? node) where T : class
    {
        if (node is null) return null;
        var typeInfo = (JsonTypeInfo<T>)CodingPackJsonContext.Default.GetTypeInfo(typeof(T))!;
        return node.Deserialize(typeInfo);
    }
}
