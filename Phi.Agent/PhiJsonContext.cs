using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Phi.Agent;

/// <summary>
/// System.Text.Json source-generated metadata for every type Phi.Agent
/// (de)serializes on the persistence boundary. Required for NativeAOT:
/// the reflection-based serializer would break at runtime once trimmed.
/// <para>
/// Wire shape matches the previous reflection-based options: PascalCase
/// property names, no indentation, no custom ignore rules — so existing
/// jsonl transcripts round-trip unchanged.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SessionEntry))]
[JsonSerializable(typeof(UserSessionEntry))]
[JsonSerializable(typeof(AssistantSessionEntry))]
[JsonSerializable(typeof(ToolResultSessionEntry))]
[JsonSerializable(typeof(CompactionSessionEntry))]
[JsonSerializable(typeof(CompactionDetails))]
[JsonSerializable(typeof(ContentBlock))]
[JsonSerializable(typeof(TextBlock))]
[JsonSerializable(typeof(ImageBlock))]
[JsonSerializable(typeof(ThinkingBlock))]
[JsonSerializable(typeof(ToolCall))]
[JsonSerializable(typeof(UserContent))]
[JsonSerializable(typeof(TextUserContent))]
[JsonSerializable(typeof(BlocksUserContent))]
[JsonSerializable(typeof(List<ContentBlock>))]
[JsonSerializable(typeof(IReadOnlyList<ContentBlock>))]
[JsonSerializable(typeof(Usage))]
[JsonSerializable(typeof(UsageCost))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
public partial class PhiJsonContext : JsonSerializerContext;
