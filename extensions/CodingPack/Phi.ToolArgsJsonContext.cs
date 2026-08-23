using System.Text.Json.Serialization;
using Phi.Extensions.CodingPack.Tools;

// Deliberately in the `Phi` namespace: the Phi.SchemaGen source generator
// emits `Phi.ToolArgsJsonContext.Default.{ArgsType}` for every
// TypedTool<TArgs> subclass. This assembly defines its OWN copy of that
// context (internal to CodingPack) so the generated code compiles here
// without Phi core needing to reference CodingPack. Same trick the core
// uses for its own ToolArgsJsonContext.
namespace Phi;

/// <summary>
/// System.Text.Json source-generated metadata for CodingPack's tool
/// argument records (<see cref="ReadArgs"/> etc.). Strict options: camelCase
/// property names (what models emit), case-insensitive matching, unknown
/// fields rejected so typos surface as validation errors the model can fix.
/// <para>
/// Consumed by the <c>Phi.SchemaGen</c>-generated <c>TypedTool&lt;T&gt;</c>
/// glue (it hardcodes the <c>Phi.ToolArgsJsonContext</c> name). Required for
/// NativeAOT.
/// </para>
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
internal sealed partial class ToolArgsJsonContext : JsonSerializerContext;
