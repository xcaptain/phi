using System.Text.Json.Serialization;
using PhiCoding.Tools;
using PhiCoding.Tools.Details;

namespace PhiCoding;

/// <summary>
/// System.Text.Json source-generated metadata for PhiCoding's persisted
/// records and tool-result details. Required for NativeAOT (reflection-based
/// serialization would break once trimmed).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SessionRecord))]
[JsonSerializable(typeof(ReadDetails))]
[JsonSerializable(typeof(WriteDetails))]
[JsonSerializable(typeof(EditDetails))]
[JsonSerializable(typeof(EditOpDetails))]
[JsonSerializable(typeof(BashDetails))]
public partial class PhiJsonContext : JsonSerializerContext;

/// <summary>
/// Strict options for deserializing LLM-supplied tool arguments: camelCase
/// property names (what models emit), case-insensitive matching, and unknown
/// fields rejected so typos surface as validation errors the model can fix.
/// Mirrors the pre-AOT <c>TypedTool.StrictJsonOptions</c>.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ReadArgs))]
[JsonSerializable(typeof(WriteArgs))]
[JsonSerializable(typeof(EditArgs))]
[JsonSerializable(typeof(EditOp))]
[JsonSerializable(typeof(BashArgs))]
public partial class ToolArgsJsonContext : JsonSerializerContext;
