namespace PhiCoding.Tui;

/// <summary>
/// Slash-command matching and completion for the prompt. Pure logic —
/// execution is wired up in <see cref="PhiTuiApp"/>. The command list comes
/// from <see cref="SlashCommandCatalog"/>; this class only matches and
/// completes against it.
/// </summary>
internal static class SlashCommands
{
    public static IReadOnlyList<string> All { get; } =
        [.. SlashCommandCatalog.All.Select(c => c.Name)];

    /// <summary>
    /// Commands that additionally accept an argument (<c>/connect &lt;provider&gt;</c>,
    /// <c>/models &lt;model&gt;</c>). Everything else must be typed exactly.
    /// </summary>
    private static readonly string[] ArgCommands =
        [.. SlashCommandCatalog.All.Where(c => c.SupportsArgs).Select(c => c.Name)];

    /// <summary>
    /// Returns the canonical command when the whole input is exactly a known
    /// slash command, otherwise null (input should go to the LLM).
    /// </summary>
    public static string? Match(string text) =>
        All.FirstOrDefault(c => c.Equals(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the canonical command plus its trailing arguments when the
    /// input starts with an argument-taking command, otherwise null. Unlike
    /// <see cref="Match"/>, a bare command with no args returns null so the
    /// exact-match picker path handles it.
    /// </summary>
    public static (string Command, string Args)? MatchWithArgs(string text)
    {
        var trimmed = text.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace <= 0) return null;

        var name = trimmed[..firstSpace];
        var canonical = ArgCommands.FirstOrDefault(
            c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (canonical is null) return null;

        var args = trimmed[(firstSpace + 1)..].Trim();
        return args.Length == 0 ? null : (canonical, args);
    }

    /// <summary>
    /// Completion candidates for the given input prefix. Only inputs starting
    /// with '/' complete; anything else yields no candidates.
    /// </summary>
    public static IReadOnlyList<string> Complete(string prefix)
    {
        if (prefix.Length == 0 || prefix[0] != '/')
        {
            return [];
        }

        return [.. All.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
    }
}
