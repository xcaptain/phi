namespace Phi.Tests;

/// <summary>
/// Shared TUnit <c>NotInParallel</c> collection keys for tests that build or
/// render XenoAtom.Terminal.UI visuals.
///
/// XenoAtom's <c>BindingManager.Current</c> is a process-wide singleton with
/// plain mutable fields (tracking context stack, pooled contexts, deferred
/// action queue) — it is NOT thread-safe. Any two tests that construct a
/// <c>State&lt;T&gt;</c>, render a visual, or enumerate a <c>DocumentFlow</c>
/// while another test holds an active tracking session can corrupt the shared
/// tracking state. That race surfaces intermittently as
/// "Collection was modified; enumeration operation may not execute" inside
/// <c>Visual.Measure</c> → <c>ReplaceDependencies</c>.
///
/// Every test touching the library must therefore share ONE constraint key so
/// TUnit runs them sequentially instead of in parallel.
/// </summary>
internal static class TuiTestGroups
{
    /// <summary>
    /// NotInParallel key for all tests that interact with XenoAtom's shared
    /// binding/tracking state.
    /// </summary>
    public const string BindingManager = "xenoatom-tui-binding-manager-tests";
}
