using Phi.Agent;
using Phi.Extensions.Events;

namespace Phi.Extensions.Host;

/// <summary>
/// Sprint 2: `/reload` — unloads all currently loaded extensions, re-discovers
/// and re-initializes them against the same session. Mirrors
/// <c>docs/extensions.md §7.2</c>.
/// <list type="bullet">
/// <item>Old <see cref="ExtensionGeneration"/>s are invalidated so captured
/// <see cref="IPhiApi"/> references from before the reload throw on any
/// action call (GenerationGuard).</item>
/// <item>Every per-extension ALC is unloaded; the loaded assemblies become
/// eligible for GC (verified via <see cref="WeakReference"/> in tests).</item>
/// <item>New extensions are loaded from the same paths and their
/// <c>Setup</c> is re-run against a fresh <see cref="PhiApi"/>.</item>
/// </list>
/// </summary>
internal sealed class ExtensionReloader
{
    private readonly ExtensionRuntime _runtime;
    private readonly IReadOnlyList<string> _assemblyPaths;

    public ExtensionReloader(ExtensionRuntime runtime, IEnumerable<string> assemblyPaths)
    {
        _runtime = runtime;
        _assemblyPaths = assemblyPaths.ToList();
    }

    /// <summary>
    /// Performs a reload and returns the new runtime. The OLD runtime is
    /// disposed inside (its ALCs unloaded); the new one replaces it. The
    /// session is unchanged — only the extension set is rebuilt.
    /// </summary>
    public ExtensionRuntime Reload()
    {
        // 1. Invalidate every generation so old PhiApi references throw.
        _runtime.InvalidateAllGenerations();

        // 2. Drop old-extension tools from the harness BEFORE unloading their
        //    ALCs — otherwise the harness keeps strong references to the old
        //    assembly, which defeats the collectible-ALC GC unload.
        _runtime.Session.RemoveExtensionTools();

        // 3. Unload old ALCs (GC dance happens at the call site after this
        //    returns — we can't force GC mid-return).
        _runtime.Dispose();

        // 4. Build a fresh runtime on the same session + bridge.
        var next = new ExtensionRuntime(_runtime.Session, _runtime.UiBridge);
        next.DiscoverAndLoad(_assemblyPaths);
        next.Initialize();

        return next;
    }
}
