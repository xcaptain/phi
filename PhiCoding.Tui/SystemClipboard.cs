using System.Diagnostics;

namespace PhiCoding.Tui;

/// <summary>
/// Writes text to the host operating system's clipboard using the standard
/// platform utilities (<c>pbcopy</c> on macOS, <c>clip.exe</c> on Windows,
/// <c>wl-copy</c>/<c>xclip</c>/<c>xsel</c> on Linux). The terminal emulator's
/// built-in clipboard (XenoAtom.Terminal's <c>TerminalClipboard</c>) reports
/// success on macOS but never actually updates the system clipboard because
/// most terminal emulators either strip the OSC 52 escape or never forward
/// it to the host, so we shell out instead.
/// </summary>
internal static class SystemClipboard
{
    /// <summary>
    /// Test seam: tests replace this with an in-memory recorder instead of
    /// spawning a real <c>pbcopy</c> process.
    /// </summary>
    internal static Func<string, bool>? Override { get; set; }

    /// <summary>
    /// Tries to copy <paramref name="text"/> to the OS clipboard. Returns
    /// <see langword="true"/> when the platform helper reported success.
    /// </summary>
    public static bool TrySetText(string text)
    {
        if (text is null)
        {
            return false;
        }

        var overrideSetter = Override;
        if (overrideSetter is not null)
        {
            // A misbehaving test override must not crash the UI thread —
            // treat any thrown exception as a failed copy and let the
            // caller decide whether to show a toast.
            try
            {
                return overrideSetter(text);
            }
            catch
            {
                return false;
            }
        }

        var command = ResolveCopyCommand();
        if (command is null)
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command.Value.FileName,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in command.Value.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            // The platform helpers read from stdin until EOF, so we write
            // the text and close the stream rather than appending a
            // platform-specific terminator.
            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.StandardOutput.Close();
            process.StandardError.Close();

            return process.WaitForExit(TimeSpan.FromSeconds(2)) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static CopyCommand? ResolveCopyCommand()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new CopyCommand("pbcopy", []);
        }

        if (OperatingSystem.IsWindows())
        {
            // clip.exe ignores stdin once EOF arrives and copies everything
            // it received to the Windows clipboard.
            return new CopyCommand("clip", []);
        }

        if (OperatingSystem.IsLinux())
        {
            // Wayland first, then X11 helpers. Each command below takes
            // its input from stdin; the first one we can spawn wins.
            foreach (var candidate in LinuxCandidates)
            {
                if (CommandExists(candidate.FileName))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static readonly CopyCommand[] LinuxCandidates =
    [
        new CopyCommand("wl-copy", []),
        new CopyCommand("xclip", ["-selection", "clipboard"]),
        new CopyCommand("xsel", ["--clipboard", "--input"]),
    ];

    private static bool CommandExists(string fileName)
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (probe is null)
            {
                return false;
            }

            probe.StandardInput.WriteLine(fileName);
            probe.StandardInput.Close();
            var stdout = probe.StandardOutput.ReadToEnd();
            probe.StandardOutput.Close();
            probe.StandardError.Close();
            return probe.WaitForExit(TimeSpan.FromSeconds(1))
                && probe.ExitCode == 0
                && !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct CopyCommand(string FileName, string[] Arguments);
}
