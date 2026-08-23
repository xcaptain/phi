using System.Reflection;

namespace Phi.Extensions.Host;

/// <summary>
/// One successfully loaded extension. <see cref="Instance"/> is created
/// (and the assembly kept alive) for the lifetime of the
/// <see cref="ExtensionRuntime"/> that owns this record. When the runtime
/// disposes, the per-extension <see cref="Alc"/> is unloaded, the
/// assembly becomes eligible for GC, and <see cref="Instance"/> becomes
/// unusable.
/// </summary>
/// <param name="Name">From <c>[PhiExtension(Name = ...)]</c>.</param>
/// <param name="Version">From <c>[PhiExtension(Version = ...)]</c>.</param>
/// <param name="Description">From <c>[PhiExtension(Description = ...)]</c>.</param>
/// <param name="EntryType">The concrete <see cref="Type"/> implementing <see cref="IPhiExtension"/>.</param>
/// <param name="Instance">Live instance (after <c>Activator.CreateInstance</c>).</param>
/// <param name="AssemblyPath">Absolute path on disk — kept for diagnostics and audit log.</param>
/// <param name="Assembly">Loaded <see cref="System.Reflection.Assembly"/> reference; prevents GC while alive.</param>
/// <param name="Alc">The per-extension <see cref="ExtensionLoadContext"/>.</param>
internal sealed record LoadedExtension(
    string Name,
    string Version,
    string Description,
    Type EntryType,
    IPhiExtension Instance,
    string AssemblyPath,
    Assembly Assembly,
    ExtensionLoadContext Alc);
