namespace PhiCoding.Slash;

/// <summary>
/// One slash command's metadata: name, one-line description, optional usage
/// hint, and whether it takes an argument. The single source of truth for the
/// command list — the suggestion strip renders it, the command matcher and
/// <c>/models</c>-style arg commands derive from it.
/// </summary>
public sealed record SlashCommandDef(
    string Name,
    string Description,
    string Usage = "",
    bool SupportsArgs = false);

/// <summary>
/// The built-in slash commands. Add a command here to make it appear in
/// autocompletion; wire its execution in <c>PhiTuiApp</c>.
/// </summary>
public static class SlashCommandCatalog
{
    public static readonly IReadOnlyList<SlashCommandDef> All =
    [
        new("/new", "Start a new, empty session."),
        new("/connect", "Connect an LLM provider (API key).", "/connect [provider]", SupportsArgs: true),
        new("/models", "Switch provider/model across configured providers."),
        new("/sessions", "Browse and resume previous sessions."),
        new("/exit", "Quit Phi."),
    ];

    /// <summary>
    /// Returns the definition by name, or null when unknown. Accepts both
    /// <c>"/connect"</c> and <c>"connect"</c>, case-insensitively.
    /// </summary>
    public static SlashCommandDef? Find(string name)
    {
        var normalized = name.TrimStart('/');
        return All.FirstOrDefault(
            c => c.Name.TrimStart('/').Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
