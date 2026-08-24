using System.Text.Json.Serialization;

namespace Phi.Extensions.Host;

/// <summary>
/// System.Text.Json source-generated metadata for the extension audit log.
/// Required for NativeAOT (and under .NET 10's
/// <c>IsReflectionEnabledByDefault=false</c> default) — a plain
/// <see cref="System.Text.Json.JsonSerializerOptions"/> with no
/// <c>TypeInfoResolver</c> throws the famous
/// <c>JsonSerializer.Serialize</c> / <c>Deserialize</c> call.
///
/// <para>
/// <c>PropertyNamingPolicy = CamelCase</c> preserves the on-disk JSONL
/// shape that the original reflection-based options used
/// (<c>"kind"</c>, <c>"extension"</c>, etc.); tests grep for these
/// keys directly. <c>WriteIndented = false</c> keeps each event on
/// one line for <c>tail -f</c> / log ingestion.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuditEvent))]
internal sealed partial class AuditLogJsonContext : JsonSerializerContext;
