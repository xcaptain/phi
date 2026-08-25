using System.Collections;
using System.Text.Json.Nodes;

namespace Phi.Extensions.Host;

/// <summary>
/// Converts an extension's <c>IReadOnlyDictionary&lt;string, object?&gt;</c>
/// payload into a <see cref="JsonNode"/> without relying on reflection-based
/// serialization (which is disabled at runtime). Handles the value types
/// extensions realistically store: primitives, strings, <see cref="JsonNode"/>,
/// nested dictionaries, and enumerables. Anything else throws a descriptive
/// error so the extension author knows to pre-serialize.
/// </summary>
internal static class ExtensionDataSerializer
{
    public static JsonNode? ToJsonNode(IReadOnlyDictionary<string, object?>? data)
    {
        if (data is null) return null;
        var obj = new JsonObject();
        foreach (var kv in data)
            obj[kv.Key] = ToNode(kv.Value);
        return obj;
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode n => n.DeepClone(),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        float f => JsonValue.Create(f),
        double d => JsonValue.Create(d),
        decimal m => JsonValue.Create(m),
        Guid g => JsonValue.Create(g),
        DateTime dt => JsonValue.Create(dt),
        DateTimeOffset dto => JsonValue.Create(dto),
        IReadOnlyDictionary<string, object?> dict => ToJsonNode(dict),
        IDictionary<string, object?> dict => ToJsonNode(new Dictionary<string, object?>(dict)),
        IEnumerable e => new JsonArray(e.Cast<object?>().Select(ToNode).ToArray()),
        _ => throw new NotSupportedException(
            $"Extension data value of type {value.GetType().FullName} is not serializable. " +
            "Use primitives, strings, JsonNode, nested dictionaries, or lists."),
    };
}
