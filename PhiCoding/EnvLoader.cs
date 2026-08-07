namespace PhiCoding;

/// <summary>
/// Loads <c>.env</c>-style files from the current working directory into the
/// process environment. <c>dotnet</c> doesn't auto-load these files; Phi
/// historically did this in <c>Program.cs</c> at startup. Lifted to the
/// shared library so the TUI and the desktop exe share the same loader.
/// </summary>
public static class EnvLoader
{
    /// <summary>
    /// Reads <c>.env</c> from <paramref name="directory"/> (defaulting to the
    /// current working directory) and exports each <c>KEY=value</c> line via
    /// <see cref="Environment.SetEnvironmentVariable(string, string?)"/>.
    /// Lines that are blank or start with <c>#</c> are skipped; values may be
    /// wrapped in single or double quotes which are stripped.
    /// </summary>
    public static void LoadDotEnv(string? directory = null)
    {
        directory ??= Environment.CurrentDirectory;
        var path = Path.Combine(directory, ".env");
        if (!File.Exists(path)) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"', '\'');
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}