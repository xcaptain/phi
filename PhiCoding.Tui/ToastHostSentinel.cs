using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui;

/// <summary>
/// Workaround for a XenoAtom.Terminal.UI 3.8.1 <see cref="ToastHost"/> bug.
/// <para>
/// When every toast has expired, the host's animation clock
/// (<c>_lastAnimationTick</c>) stops advancing because <c>AdvanceAnimation</c>
/// is only called while toasts are present. The next toast added later is then
/// treated as having been on screen for the whole idle gap and is dismissed
/// instantly — so it never appears.
/// </para>
/// <para>
/// This is why the demo appears to "always show a toast": its toasts are
/// triggered by rapid button clicks that land inside each other's duration,
/// so the clock never goes stale. Copying text is sporadic (gaps often exceed
/// the 3s toast duration), which trips the bug.
/// </para>
/// <para>
/// <see cref="Install"/> keeps one invisible, never-expiring sentinel toast
/// alive so the entry list is never empty and the clock never goes stale —
/// every later <see cref="ToastService"/> toast survives its full duration,
/// matching the demo's behaviour. Remove this once the upstream fix is
/// released and Phi upgrades the NuGet package.
/// </para>
/// </summary>
internal static class ToastHostSentinel
{
    /// <summary>
    /// Adds an invisible, never-expiring toast to <paramref name="toastHost"/>
    /// that keeps its animation clock warm. Safe to call before the app loop
    /// starts; the sentinel has no visual footprint.
    /// </summary>
    public static void Install(ToastHost toastHost)
    {
        ArgumentNullException.ThrowIfNull(toastHost);

        toastHost.Show(() => new Toast
        {
            Content = new TextBlock(""),
            Severity = ToastSeverity.Info,
            // Explicit null (not DefaultDuration): never auto-dismiss, so the
            // entry list never becomes empty.
            Duration = null,
            IsVisible = false,
        });
    }
}
