using System.Text.Json.Serialization;
using Phi.Extensions.CustomCardDemo;

// Deliberately in the `Phi` namespace: the Phi.SchemaGen source generator
// emits `Phi.ToolArgsJsonContext.Default.{ArgsType}` for every
// TypedTool<TArgs> subclass. This assembly defines its own copy of that
// context so the generated code compiles here without Phi core needing to
// reference CustomCardDemo.
namespace Phi;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DemoArgs))]
internal sealed partial class ToolArgsJsonContext : JsonSerializerContext;
