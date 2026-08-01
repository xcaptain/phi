using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiProvider;

/// <summary>
/// Accumulates streamed tool call deltas into a single <see cref="ToolCall"/>.
/// OpenAI sends <c>tool_calls[].function.arguments</c> as a JSON string
/// fragment that has to be concatenated across chunks before parsing.
/// </summary>
internal sealed class ToolCallBuilder
{
    public string? Id { get; private set; }
    public string? Name { get; private set; }
    private readonly StringBuilder _argumentsBuffer = new();

    public void AddDelta(JsonElement toolCallDelta)
    {
        if (toolCallDelta.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String)
        {
            var newId = id.GetString();
            if (!string.IsNullOrEmpty(newId)) Id = newId;
        }

        if (toolCallDelta.TryGetProperty("function", out var function))
        {
            if (function.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String)
            {
                var newName = name.GetString();
                if (!string.IsNullOrEmpty(newName)) Name = newName;
            }

            if (function.TryGetProperty("arguments", out var args) &&
                args.ValueKind == JsonValueKind.String)
            {
                var fragment = args.GetString();
                if (fragment is not null) _argumentsBuffer.Append(fragment);
            }
        }
    }

    public ToolCall Build() => new(Id ?? "", Name ?? "")
    {
        Arguments = ParseArguments(_argumentsBuffer.ToString()),
    };

    private static JsonObject ParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try
        {
            var node = JsonNode.Parse(json);
            return node as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }
}
