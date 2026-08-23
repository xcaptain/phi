using System.Text.Json.Serialization;
using Phi.Providers;

namespace Phi;

/// <summary>
/// System.Text.Json source-generated metadata for Phi's persisted records.
/// Required for NativeAOT (reflection-based serialization would break once
/// trimmed).
/// <para>
/// Sprint 2.5: the tool args + tool-result details moved out of the core
/// into the CodingPack extension — each extension carries its own AOT
/// context (CodingPack's <c>ToolArgsJsonContext</c> / <c>CodingPackJsonContext</c>).
/// </para>
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SessionRecord))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(PhiSettings))]
internal sealed partial class PhiJsonContext : JsonSerializerContext;
