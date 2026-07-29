namespace PhiCoding.Tui;

/// <summary>
/// Slash-command matching and completion for the prompt. Pure logic —
/// execution is wired up in <see cref="PhiTuiApp"/>.
/// </summary>
internal static class SlashCommands
{
    public static readonly IReadOnlyList<string> All = ["/exit", "/sessions"];

    /// <summary>
    /// Returns the canonical command when the whole input is exactly a known
    /// slash command, otherwise null (input should go to the LLM).
    /// </summary>
    public static string? Match(string text) =>
        All.FirstOrDefault(c => c.Equals(text, StringComparison.OrdinalIgnoreCase));

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

        return All.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
