using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Phi.Extensions.Host;

/// <summary>
/// Per-extension <see cref="AssemblyLoadContext"/> (ALC). Each loaded
/// extension gets its own collectible ALC, so:
/// <list type="bullet">
/// <item>Two extensions using different versions of Newtonsoft.Json don't
/// fight each other — the per-extension ALC resolves deps from the
/// extension's own directory first.</item>
/// <item><c>/reload</c> (Sprint 2) calls <c>Unload()</c> on each ALC; the
/// .NET runtime then GCs the loaded assembly after a few Gen2
/// collections.</item>
/// </list>
/// <para>
/// Sprint 4 bundle support: an extension shipped as a <c>bundle</c> (its
/// own <c>runtimes/</c> folder) gets two resolution hooks:
/// <list type="bullet">
/// <item><see cref="Load"/> resolves managed deps under
/// <c>runtimes/{rid}/lib/</c> (standard .NET bundle layout, e.g.
/// <c>runtimes/osx-arm64/lib/net10.0/Newtonsoft.Json.dll</c>).</item>
/// <item><see cref="InstallNativeResolver"/> installs a
/// <c>NativeLibrary.SetDllImportResolver</c> on the extension assembly that
/// maps P/Invoke names to <c>runtimes/{rid}/native/lib*.dylib</c> etc. —
/// SkiaSharp / SQLitePCLRaw style native deps.</item>
/// </list>
/// A single bundle is therefore cross-platform: each platform picks its own
/// <c>{rid}</c> directory and Phi never hard-codes a platform name.
/// </para>
/// <para>
/// <c>isCollectible: true</c> is the key bit — without it, <c>Unload()</c>
/// throws. The cost of collectibility is a small amount of bookkeeping per
/// ALC.
/// </para>
/// </summary>
internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly string _extensionDir;

    public ExtensionLoadContext(string extensionDir)
        : base(isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionDir);
        _extensionDir = extensionDir;
    }

    /// <summary>
    /// Resolves a managed dependency of the extension. Looks under
    /// <c>runtimes/{rid}/lib/</c> first (bundle layout), then lets the
    /// default resolution take over. Returns <c>null</c> when not found so
    /// the runtime falls through to the host's assemblies.
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var candidate = ResolveManagedBundleAssemblyPath(assemblyName);
        return candidate is null ? null : LoadFromAssemblyPath(candidate);
    }

    /// <summary>
    /// Installs a <see cref="NativeLibrary.SetDllImportResolver"/> on
    /// <paramref name="assembly"/> that maps P/Invoke library names to
    /// <c>runtimes/{rid}/native/</c>. Called once right after the extension's
    /// main assembly is loaded, so the resolver is in place before any
    /// extension code runs. No-op when the bundle has no <c>native/</c>
    /// directory for the current RID.
    /// </summary>
    public void InstallNativeResolver(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        NativeLibrary.SetDllImportResolver(assembly, (libName, _, _) =>
        {
            var libPath = ResolveNativeBundleLibraryPath(libName);
            return libPath is null ? IntPtr.Zero : NativeLibrary.Load(libPath);
        });
    }

    /// <summary>
    /// Resolves a managed bundle dependency path under
    /// <c>runtimes/{rid}/lib/</c>, or <c>null</c> when the bundle has no
    /// matching assembly. Kept <c>internal</c> so tests can verify the
    /// path-selection logic without needing to compile a real dependent dll.
    /// </summary>
    internal string? ResolveManagedBundleAssemblyPath(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        var rid = RuntimeInformation.RuntimeIdentifier;
        var libRoot = Path.Combine(_extensionDir, "runtimes", rid, "lib");
        if (!Directory.Exists(libRoot)) return null;

        return Directory
            .EnumerateFiles(libRoot, $"{assemblyName.Name}.dll", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// <summary>
    /// Resolves a native bundle library path under
    /// <c>runtimes/{rid}/native/</c>, or <c>null</c> when the bundle has no
    /// matching file for the current platform naming convention.
    /// <para>
    /// Windows uses <c>{name}.dll</c>; macOS uses <c>lib{name}.dylib</c>;
    /// Linux uses <c>lib{name}.so</c>. Kept <c>internal</c> so tests can
    /// verify the mapping logic without needing a real native binary.
    /// </para>
    /// </summary>
    internal string? ResolveNativeBundleLibraryPath(string libName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libName);

        var nativeDir = Path.Combine(_extensionDir, "runtimes",
            RuntimeInformation.RuntimeIdentifier, "native");
        if (!Directory.Exists(nativeDir)) return null;

        var fileName = OperatingSystem.IsWindows() ? $"{libName}.dll" :
            OperatingSystem.IsMacOS() ? $"lib{libName}.dylib" :
                                        $"lib{libName}.so";
        var libPath = Path.Combine(nativeDir, fileName);
        return File.Exists(libPath) ? libPath : null;
    }
}
