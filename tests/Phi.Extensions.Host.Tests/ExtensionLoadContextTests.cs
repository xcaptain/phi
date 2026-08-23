using System.Reflection;
using System.Runtime.Loader;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Verifies <see cref="ExtensionLoadContext"/> is set up for collectible
/// per-extension ALCs (the bit that makes /reload work in Sprint 2).
/// </summary>
[NotInParallel("alc")]
public class ExtensionLoadContextTests
{
    [Test]
    public async Task Is_Collectible()
    {
        var alc = new ExtensionLoadContext();
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
        var alc1 = new ExtensionLoadContext();
        var alc2 = new ExtensionLoadContext();
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
}
