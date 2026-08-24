using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Verifies <see cref="ExtensionLoadContext"/> is set up for collectible
/// per-extension ALCs (the bit that makes /reload work in Sprint 2) and that
/// Sprint 4's bundle support (runtimes/{rid}/ lib + native resolution) works.
/// </summary>
[NotInParallel("alc")]
public class ExtensionLoadContextTests : IDisposable
{
    private readonly string _bundleDir;

    public ExtensionLoadContextTests()
    {
        _bundleDir = Path.Combine(Path.GetTempPath(), $"phi-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_bundleDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_bundleDir, recursive: true); } catch { /* best-effort */ }
    }

    [Test]
    public async Task Is_Collectible()
    {
        var alc = new ExtensionLoadContext(Path.GetTempPath());
        try
        {
            // Internally AssemblyLoadContext exposes IsCollectible as
            // a public bool property in .NET 9+; reflect to check across versions.
            var prop = typeof(AssemblyLoadContext).GetProperty(
                "IsCollectible",
                BindingFlags.Public | BindingFlags.Instance);
            await Assert.That(prop).IsNotNull();
            var value = (bool)prop!.GetValue(alc)!;
            await Assert.That(value).IsTrue();
        }
        finally
        {
            try { alc.Unload(); } catch { /* best-effort */ }
        }
    }

    [Test]
    public async Task Each_Instance_Is_Independent()
    {
        // Two ALCs created from the same source must be different instances —
        // this is what enables per-extension isolation (Newtonsoft.Json 12
        // in ext A doesn't see ext B's 13).
        var alc1 = new ExtensionLoadContext(Path.GetTempPath());
        var alc2 = new ExtensionLoadContext(Path.GetTempPath());
        try
        {
            await Assert.That(alc1).IsNotSameReferenceAs(alc2);
        }
        finally
        {
            try { alc1.Unload(); } catch { /* best-effort */ }
            try { alc2.Unload(); } catch { /* best-effort */ }
        }
    }

    [Test]
    public async Task ResolveManagedBundleAssemblyPath_FindsDll_UnderRuntimesRidLib()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var libDir = Path.Combine(_bundleDir, "runtimes", rid, "lib", "net10.0");
        Directory.CreateDirectory(libDir);
        var dllPath = Path.Combine(libDir, "Newtonsoft.Json.dll");
        File.WriteAllBytes(dllPath, [0x4d, 0x5a]); // stub file; path resolution only

        var alc = new ExtensionLoadContext(_bundleDir);
        try
        {
            var resolved = alc.ResolveManagedBundleAssemblyPath(new AssemblyName("Newtonsoft.Json"));
            await Assert.That(resolved).IsEqualTo(dllPath);
        }
        finally
        {
            try { alc.Unload(); } catch { /* best-effort */ }
        }
    }

    [Test]
    public async Task ResolveNativeBundleLibraryPath_MapsToPlatformFileName()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var nativeDir = Path.Combine(_bundleDir, "runtimes", rid, "native");
        Directory.CreateDirectory(nativeDir);

        var fileName = OperatingSystem.IsWindows() ? "foo.dll" :
            OperatingSystem.IsMacOS() ? "libfoo.dylib" :
                                        "libfoo.so";
        var libPath = Path.Combine(nativeDir, fileName);
        File.WriteAllBytes(libPath, [0x00]); // stub file; path resolution only

        var alc = new ExtensionLoadContext(_bundleDir);
        try
        {
            var resolved = alc.ResolveNativeBundleLibraryPath("foo");
            await Assert.That(resolved).IsEqualTo(libPath);
        }
        finally
        {
            try { alc.Unload(); } catch { /* best-effort */ }
        }
    }

    [Test]
    public async Task ResolveNativeBundleLibraryPath_ReturnsNull_WhenMissing()
    {
        var alc = new ExtensionLoadContext(_bundleDir);
        try
        {
            var resolved = alc.ResolveNativeBundleLibraryPath("missing-lib");
            await Assert.That(resolved).IsNull();
        }
        finally
        {
            try { alc.Unload(); } catch { /* best-effort */ }
        }
    }

    [Test]
    public async Task InstallNativeResolver_WithoutNativeDir_DoesNotThrow()
    {
        var alc = new ExtensionLoadContext(_bundleDir);
        try
        {
            alc.InstallNativeResolver(typeof(ExtensionLoadContextTests).Assembly);
            await Assert.That(true).IsTrue();
        }
        finally
        {
            try { alc.Unload(); } catch { /* best-effort */ }
        }
    }
}
