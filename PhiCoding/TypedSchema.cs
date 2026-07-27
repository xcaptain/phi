using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;

namespace PhiCoding;

/// <summary>
/// Generates JSON Schema from .NET types via reflection.
/// Cached so repeated calls for the same type are O(1).
/// Recognizes C# 11+ <c>required</c> keyword and <see cref="DescriptionAttribute"/>.
/// </summary>
public static class TypedSchema
{
    private static readonly ConcurrentDictionary<Type, JsonObject> Cache = new();

    public static JsonObject For<T>() => For(typeof(T));

    public static JsonObject For(Type type) => Cache.GetOrAdd(type, Build);

    private static JsonObject Build(Type type)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = ToCamelCase(prop.Name);
            properties[name] = BuildProperty(prop);
            if (IsRequired(prop))
                required.Add(name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        };
    }

    private static JsonObject BuildProperty(PropertyInfo prop)
    {
        var schema = BuildTypeSchema(prop.PropertyType);

        var desc = prop.GetCustomAttribute<DescriptionAttribute>();
        if (desc is not null)
            schema["description"] = desc.Description;

        return schema;
    }

    private static JsonObject BuildTypeSchema(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string)) return new JsonObject { ["type"] = "string" };
        if (underlying == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (underlying == typeof(byte) || underlying == typeof(sbyte) ||
            underlying == typeof(short) || underlying == typeof(ushort) ||
            underlying == typeof(int) || underlying == typeof(uint) ||
            underlying == typeof(long) || underlying == typeof(ulong))
            return new JsonObject { ["type"] = "integer" };
        if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal))
            return new JsonObject { ["type"] = "number" };

        if (underlying.IsArray)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = BuildTypeSchema(underlying.GetElementType()!),
            };
        }

        // Nested record/object — recurse
        if (!underlying.IsPrimitive && underlying != typeof(object) && underlying != typeof(string))
        {
            return Build(underlying);
        }

        return new JsonObject { ["type"] = "object" };
    }

    private static bool IsRequired(PropertyInfo prop)
    {
        // C# 11+ 'required' keyword emits RequiredMemberAttribute
        if (prop.CustomAttributes.Any(a => a.AttributeType.Name == "RequiredMemberAttribute"))
            return true;
        // Explicit JsonRequiredAttribute (System.Text.Json)
        if (prop.GetCustomAttribute<System.Text.Json.Serialization.JsonRequiredAttribute>() is not null)
            return true;
        return false;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0])) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}