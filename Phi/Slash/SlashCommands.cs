namespace Phi.Slash;

/// <summary>
/// Slash-command matching and completion for the prompt. Pure logic —
/// execution is wired up in the UI layer (TUI's <c>PhiTuiApp</c>, future
/// desktop shell). The command list comes from
/// <see cref="SlashCommandCatalog"/>; this class only matches and completes
/// against it.
/// </summary>
public static class SlashCommands
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
    /// Matches the <c>/skill:NAME [prompt]</c> pattern (command + argument
    /// fused with a colon, optional trailing prompt after whitespace).
    /// Returns the skill name and any trailing prompt, or null when the
    /// input is not a skill invocation. The skill name is everything up to
    /// the first whitespace after <c>/skill:</c> — the rest is the prompt.
    /// </summary>
    public static (string SkillName, string? Prompt)? MatchSkill(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("/skill:", StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = trimmed["/skill:".Length..].TrimStart();
        if (rest.Length == 0)
            return null;

        var space = rest.IndexOf(' ');
        if (space < 0)
            return (rest, null);

        var skillName = rest[..space];
        var prompt = rest[(space + 1)..].Trim();
        return (skillName, prompt.Length == 0 ? null : prompt);
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
