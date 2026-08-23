using System.Runtime.Loader;

namespace Phi.Extensions.Host;

/// <summary>
/// Per-extension <see cref="AssemblyLoadContext"/> (ALC). Each loaded
/// extension gets its own collectible ALC, so:
/// <list type="bullet">
/// <item>Two extensions using different versions of Newtonsoft.Json don't
/// fight each other (Sprint 1: the resolver just falls through to the
/// default; the per-extension ALC is mainly for isolation + unload).</item>
/// <item><c>/reload</c> (Sprint 2) calls <c>Unload()</c> on each ALC; the
/// .NET runtime then GCs the loaded assembly after a few Gen2
/// collections.</item>
/// </list>
/// <para>
/// <c>isCollectible: true</c> is the key bit — without it, <c>Unload()</c>
/// throws. The cost of collectibility is a small amount of bookkeeping
/// per ALC. For Sprint 1 we don't override <see cref="Load"/> or hook
/// <c>Resolving</c> yet; native deps and SkiaSharp-style cases land in
/// Sprint 4 alongside the bundle-format support from §8 of the design doc.
/// </para>
/// </summary>
internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    public ExtensionLoadContext()
        : base(isCollectible: true)
    {
    }
}
