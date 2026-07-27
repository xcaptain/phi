using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PhiAgent;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ImageBlock), "image")]
[JsonDerivedType(typeof(ThinkingBlock), "thinking")]
[JsonDerivedType(typeof(ToolCall), "toolCall")]
public abstract record ContentBlock;

public sealed record TextBlock(string Text) : ContentBlock
{
    public string? TextSignature { get; init; }
}

public sealed record ImageBlock(string Data, string MimeType) : ContentBlock;

public sealed record ThinkingBlock(string Thinking) : ContentBlock
{
    public string? ThinkingSignature { get; init; }
    public bool Redacted { get; init; }
}

public sealed record ToolCall(string Id, string Name) : ContentBlock
{
    public JsonObject Arguments { get; init; } = new();
    public string? ThoughtSignature { get; init; }
}

[JsonConverter(typeof(UserContentConverter))]
public abstract record UserContent
{
    public static UserContent FromText(string text) => new TextUserContent(text);
    public static UserContent FromBlocks(IReadOnlyList<ContentBlock> blocks) => new BlocksUserContent(blocks);
    public static UserContent FromBlocks(params ContentBlock[] blocks) => new BlocksUserContent(blocks);

    public static implicit operator UserContent(string text) => new TextUserContent(text);
    public static implicit operator UserContent(List<ContentBlock> blocks) => new BlocksUserContent(blocks);
    public static implicit operator UserContent(ContentBlock[] blocks) => new BlocksUserContent(blocks);

    public string ExtractText() => this switch
    {
        TextUserContent t => t.Text,
        BlocksUserContent b => string.Concat(b.Blocks.OfType<TextBlock>().Select(x => x.Text)),
        _ => "",
    };
}

public sealed record TextUserContent(string Text) : UserContent;

public sealed record BlocksUserContent(IReadOnlyList<ContentBlock> Blocks) : UserContent;

public sealed class UserContentConverter : JsonConverter<UserContent>
{
    public override UserContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new TextUserContent(reader.GetString() ?? "");

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var blocks = JsonSerializer.Deserialize<List<ContentBlock>>(ref reader, options) ?? [];
            return new BlocksUserContent(blocks);
        }

        throw new JsonException($"Expected string or array for UserContent, got {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, UserContent value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case TextUserContent t:
                writer.WriteStringValue(t.Text);
                break;
            case BlocksUserContent b:
                JsonSerializer.Serialize(writer, b.Blocks, options);
                break;
        }
    }
}

public record UserMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [Required]
    public UserContent Content { get; init; } = "";

    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonIgnore]
    public string Text => Content.ExtractText();
}

public sealed record UsageCost
{
    public double Input { get; init; }
    public double Output { get; init; }
    public double CacheRead { get; init; }
    public double CacheWrite { get; init; }
    public double Total { get; init; }
}

public sealed record Usage
{
    public int Input { get; init; }
    public int Output { get; init; }
    public int CacheRead { get; init; }
    public int CacheWrite { get; init; }

    [JsonPropertyName("cacheWrite1H")]
    public int? CacheWrite1h { get; init; }

    public int? Reasoning { get; init; }
    public int TotalTokens { get; init; }
    public UsageCost Cost { get; init; } = new();
}

public static class StopReasons
{
    public const string Stop = "stop";
    public const string Length = "length";
    public const string ToolUse = "toolUse";
    public const string Error = "error";
    public const string Aborted = "aborted";
}

public sealed record AssistantDiagnosticError
{
    public string? Name { get; init; }
    public string Message { get; init; } = "";
    public string? Stack { get; init; }
    public string? Code { get; init; }
}

public sealed record AssistantMessageDiagnostic
{
    public string Type { get; init; } = "";
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public AssistantDiagnosticError? Error { get; init; }
    public IReadOnlyDictionary<string, JsonNode>? Details { get; init; }
}

public sealed record AssistantMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

    public IReadOnlyList<ContentBlock> Content { get; init; } = [];

    public string Api { get; init; } = "unknown";
    public string Provider { get; init; } = "unknown";
    public string Model { get; init; } = "unknown";
    public string? ResponseModel { get; init; }
    public string? ResponseId { get; init; }
    public IReadOnlyList<AssistantMessageDiagnostic>? Diagnostics { get; init; }
    public Usage Usage { get; init; } = new();
    public string StopReason { get; init; } = StopReasons.Stop;
    public string? ErrorMessage { get; init; }
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonIgnore]
    public string Text => string.Concat(Content.OfType<TextBlock>().Select(b => b.Text));

    [JsonIgnore]
    public string ThinkingText => string.Concat(Content.OfType<ThinkingBlock>().Select(b => b.Thinking));

    [JsonIgnore]
    public IReadOnlyList<ToolCall> ToolCalls => Content.OfType<ToolCall>().ToList();
}

public sealed record ToolResultMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "toolResult";

    public string ToolCallId { get; init; } = "";
    public string ToolName { get; init; } = "";

    public IReadOnlyList<ContentBlock> Content { get; init; } = [];

    public JsonNode? Details { get; init; }

    public IReadOnlyList<string>? AddedToolNames { get; init; }

    public bool IsError { get; init; }

    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonIgnore]
    public string Text => string.Concat(Content.OfType<TextBlock>().Select(b => b.Text));
}

public sealed record BashExecutionMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "bashExecution";

    public string Command { get; init; } = "";
    public string Output { get; init; } = "";

    public int? ExitCode { get; init; }
    public bool Cancelled { get; init; }
    public bool Truncated { get; init; }
    public string? FullOutputPath { get; init; }

    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public bool ExcludeFromContext { get; init; }
}

public sealed record CustomMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "custom";

    public string CustomType { get; init; } = "";

    public UserContent Content { get; init; } = "";

    public bool Display { get; init; } = true;
    public JsonNode? Details { get; init; }

    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonIgnore]
    public string Text => Content.ExtractText();
}

public sealed record BranchSummaryMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "branchSummary";

    public string Summary { get; init; } = "";
    public string FromId { get; init; } = "";

    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed record CompactionSummaryMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "compactionSummary";

    public string Summary { get; init; } = "";
    public int TokensBefore { get; init; }

    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
