namespace Phi.Avalonia;

/// <summary>
/// Minimal file logger for diagnosing the desktop app without a console.
/// Appends to <c>{PHI_HOME}/avalonia-debug.log</c>; write failures are
/// ignored.
/// </summary>
internal static class DeskLog
{
    public static void Write(string message)
    {
        try
        {
            var path = Path.Combine(SessionPaths.PhiHome, "avalonia-debug.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }
}
