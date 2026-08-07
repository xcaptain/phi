using Aprillz.MewUI;

namespace PhiCoding.Desk.Tests;

/// <summary>
/// Registers the MewUI graphics backend once per process so layout tests can
/// measure MewUI elements offscreen (text measurement needs a graphics
/// factory). Idempotent — the backend registrar accepts the same factory
/// repeatedly.
/// </summary>
internal static class MewTestHost
{
    private static int _registered;

    public static void EnsureBackend()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        if (OperatingSystem.IsMacOS())
            MewVGMacOSBackend.Register();
        else if (OperatingSystem.IsWindows())
            Direct2DBackend.Register();
        else if (OperatingSystem.IsLinux())
            MewVGX11Backend.Register();
    }
}