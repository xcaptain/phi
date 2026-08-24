using System.Reflection;

namespace Phi.Extensions.Host;

/// <summary>
/// Loads one extension assembly: spins up an <see cref="ExtensionLoadContext"/>,
/// loads the dll, finds the <c>[PhiExtension]</c>-attributed entry type,
/// instantiates it. <c>Setup()</c> is NOT called here — that requires a
/// session-bound <c>IPhiApi</c> and runs in
/// <see cref="ExtensionRuntime.Initialize"/>.
/// </summary>
internal static class ExtensionLoader
{
    /// <summary>
    /// Load the extension at <paramref name="assemblyPath"/>. The file
    /// must contain exactly one <c>[PhiExtension]</c>-annotated class
    /// implementing <see cref="IPhiExtension"/> (per
    /// <c>docs/extensions.md §2</c>: "v1 一个 dll 限一个 [PhiExtension] class").
    /// </summary>
    /// <exception cref="FileNotFoundException">Path doesn't exist.</exception>
    /// <exception cref="ExtensionLoadDiagnostic">
    /// Missing <c>[PhiExtension]</c>, multiple entry types, or activation
    /// throws. The diagnostics carry enough context to point the user at
    /// the bad dll + reason without leaking internal stack traces.
    /// </exception>
    public static LoadedExtension Load(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"extension assembly not found: {assemblyPath}", assemblyPath);

        var extensionDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))
            ?? Environment.CurrentDirectory;
        var alc = new ExtensionLoadContext(extensionDir);
        Assembly assembly;
        try
        {
            assembly = alc.LoadFromAssemblyPath(assemblyPath);
            // Bundle support: install the native P/Invoke resolver (reads
            // runtimes/{rid}/native/) right after the assembly loads so any
            // DllImport in the extension's code (or its deps) resolves from
            // the bundle. No-op when the bundle has no native/ directory.
            alc.InstallNativeResolver(assembly);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup; unload is a no-op if LoadFromAssemblyPath didn't complete.
            TryUnload(alc);
            throw new ExtensionLoadDiagnostic(
                $"failed to load assembly '{Path.GetFileName(assemblyPath)}': {ex.Message}", ex);
        }

        var (entryType, attribute) = FindEntryType(assembly, assemblyPath)
            ?? throw new ExtensionLoadDiagnostic(
                $"no [PhiExtension] entry type found in '{Path.GetFileName(assemblyPath)}'");

        IPhiExtension instance;
        try
        {
            instance = (IPhiExtension)Activator.CreateInstance(entryType)!;
        }
        catch (Exception ex)
        {
            TryUnload(alc);
            throw new ExtensionLoadDiagnostic(
                $"failed to instantiate '{entryType.FullName}' from '{Path.GetFileName(assemblyPath)}': {ex.Message}", ex);
        }

        return new LoadedExtension(
            Name: attribute.Name,
            Version: attribute.Version,
            Description: attribute.Description,
            EntryType: entryType,
            Instance: instance,
            AssemblyPath: Path.GetFullPath(assemblyPath),
            Assembly: assembly,
            Alc: alc,
            DeclaredCapabilities: attribute.Capabilities);
    }

    private static (Type type, PhiExtensionAttribute attr)? FindEntryType(
        Assembly assembly, string assemblyPath)
    {
        Type? match = null;
        PhiExtensionAttribute? matchAttr = null;
        var collisions = 0;

        try
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var attr in type.GetCustomAttributes<PhiExtensionAttribute>())
                {
                    if (match is null)
                    {
                        match = type;
                        matchAttr = attr;
                    }
                    else
                    {
                        collisions++;
                    }
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            // One or more types in the assembly couldn't be resolved (usually
            // missing transitive deps). Surface the first loader error.
            var inner = ex.LoaderExceptions.FirstOrDefault()?.Message ?? "unknown";
            throw new ExtensionLoadDiagnostic(
                $"failed to enumerate types in '{Path.GetFileName(assemblyPath)}': {inner}", ex);
        }

        if (collisions > 0)
        {
            throw new ExtensionLoadDiagnostic(
                $"'{Path.GetFileName(assemblyPath)}' has multiple [PhiExtension] classes; " +
                "v1 allows exactly one per assembly");
        }

        if (match is null || matchAttr is null) return null;
        return (match, matchAttr);
    }

    private static void TryUnload(ExtensionLoadContext alc)
    {
        try { alc.Unload(); } catch { /* best-effort */ }
    }
}

/// <summary>
/// One-shot diagnostic exception thrown by <see cref="ExtensionLoader"/>.
/// Surface verbatim in <c>~/.phi/logs/extensions-{date}.log</c>; show a
/// short summary in the status bar (full path + reason, no stack).
/// </summary>
public sealed class ExtensionLoadDiagnostic : Exception
{
    public ExtensionLoadDiagnostic(string message) : base(message) { }
    public ExtensionLoadDiagnostic(string message, Exception inner) : base(message, inner) { }
}
