using System.Text.Json.Serialization;
using Phi.Extensions.CodingPack.Tools.Details;

namespace Phi.Extensions.CodingPack;

/// <summary>
/// System.Text.Json source-generated metadata for the CodingPack's tool
/// <c>Details</c> records (<see cref="ReadDetails"/> etc.). Required for
/// NativeAOT: the reflection-based serializer would break once trimmed.
/// <para>
/// This is the CodingPack's own AOT context — the Phi core's
/// <c>PhiJsonContext</c> no longer knows about these types after Sprint 2.5
/// (the tools moved out of the core). The tools call
/// <see cref="ToolDetails.Node{T}"/> which reads type metadata from here.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ReadDetails))]
[JsonSerializable(typeof(WriteDetails))]
[JsonSerializable(typeof(EditDetails))]
[JsonSerializable(typeof(EditOpDetails))]
[JsonSerializable(typeof(BashDetails))]
public sealed partial class CodingPackJsonContext : JsonSerializerContext;
