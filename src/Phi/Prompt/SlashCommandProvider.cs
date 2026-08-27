using Phi.Slash;

namespace Phi.Prompt;

/// <summary>
/// <see cref="ISuggestionProvider"/> for the built-in slash commands: when
/// the buffer starts with <c>/</c> and the caret sits on that same first
/// line, returns the commands whose name matches the prefix typed so far.
/// The replacement span covers only the leading-<c>/</c> command token,
/// not any trailing arguments the user has already typed.
/// <para>
/// The strict "buffer starts with <c>/</c> on the first line" gate lives in
/// <see cref="SuggestionTrigger.StartsWithSlashOnFirstLine"/>; this provider
/// only checks the typed prefix against the command catalog. Slash commands
/// only fire on the very first line — a slash buried mid-sentence like
/// <c>"please /exit"</c>, or a command typed on a continuation line like
/// <c>"hello\n/exit"</c>, is ordinary text and produces no suggestions.
/// </para>
/// </summary>
public sealed class SlashCommandProvider(IReadOnlyList<SlashCommandDef>? commands = null) : ISuggestionProvider
{
    private readonly IReadOnlyList<SlashCommandDef> _commands = commands ?? SlashCommandCatalog.All;

    public SuggestionMatch? GetSuggestion(ReadOnlySpan<char> text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);

        // The trigger is "buffer starts with '/' on the first line and the
        // caret is still on that first line" — delegated to the shared
        // helper so the strip, the Tab-completion handler, and the
        // providers can never drift apart. Once it passes, the typed
        // prefix is the leading command token bounded by the first
        // whitespace (if any) so trailing args like "/connect openai"
        // don't pollute the catalog match.
        var asString = text.ToString();
        if (!SuggestionTrigger.StartsWithSlashOnFirstLine(asString, caret))
        {
            return null;
        }

        var tokenEnd = FindTokenEnd(asString, caret);
        var prefix = text[0..tokenEnd];

        List<SuggestionItem>? items = null;
        foreach (var command in _commands)
        {
            if (command.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                (items ??= new List<SuggestionItem>(_commands.Count)).Add(
                    new SuggestionItem(command.Name, command.Description, command.Name));
            }
        }

        return items is null || items.Count == 0
            ? null
            : new SuggestionMatch(0, tokenEnd, items);
    }

    /// <summary>
    /// Returns the index just past the first whitespace between the buffer
    /// start and <paramref name="caret"/> (or <paramref name="caret"/>
    /// itself if no whitespace appears). Bounds the prefix to the leading
    /// command token and drops trailing arguments. The newline guard is
    /// implicit: <see cref="SuggestionTrigger.StartsWithSlashOnFirstLine"/>
    /// rejects inputs whose caret is past a newline before this method
    /// runs.
    /// </summary>
    private static int FindTokenEnd(string text, int caret)
    {
        var i = 0;
        while (i < caret && !char.IsWhiteSpace(text[i]))
        {
            i++;
        }
        return i;
    }
}
