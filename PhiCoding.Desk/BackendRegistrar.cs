using Aprillz.MewUI;

namespace PhiCoding.Desk;

/// <summary>
/// Registers the MewUI platform host and graphics backend for the current
/// OS before <see cref="Application.Run"/> is called. The
/// <c>Aprillz.MewUI</c> metapackage bundles every platform host and
/// rendering backend; this picks the one matching the running OS — same
/// pattern as the upstream MewUI samples.
/// </summary>
public static class BackendRegistrar
{
    public static void Register()
    {
        if (OperatingSystem.IsMacOS())
        {
            MacOSPlatform.Register();
            MewVGMacOSBackend.Register();
        }
        else if (OperatingSystem.IsWindows())
        {
            Win32Platform.Register();
            Direct2DBackend.Register();
        }
        else if (OperatingSystem.IsLinux())
        {
            X11Platform.Register();
            MewVGX11Backend.Register();
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"Phi Desk does not support OS: {Environment.OSVersion.VersionString}");
        }
    }
}