namespace PhiCoding.Desk.Tests;

/// <summary>
/// Shared TUnit <c>NotInParallel</c> collection keys for desktop tests.
/// MewUI's <c>ObservableValue&lt;T&gt;</c> is per-instance (unlike
/// XenoAtom's process-wide binding manager), but the components under test
/// bind to shared session events synchronously — keep these keys distinct
/// so structural tests don't race each other.
/// </summary>
internal static class DeskTestGroups
{
    /// <summary>Structural component tests (no render loop).</summary>
    public const string Components = "phicoding-desk-components-tests";
}