using Phi.Slash;

namespace Phi.Prompt;

/// <summary>
/// <see cref="ISuggestionProvider"/> for the built-in slash commands: when
/// the token ending at the caret starts with <c>/</c>, returns the commands
/// matching that prefix. The replacement span covers only the current token
/// (leading <c>/</c> included), so accepting a suggestion replaces just that
/// token.
/// </summary>
public sealed class SlashCommandProvider(IReadOnlyList<SlashCommandDef>? commands = null) : ISuggestionProvider
{
    private readonly IReadOnlyList<SlashCommandDef> _commands = commands ?? SlashCommandCatalog.All;

    public SuggestionMatch? GetSuggestion(ReadOnlySpan<char> text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var start = caret;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        var prefix = text[start..caret];
        if (prefix.Length == 0 || prefix[0] != '/')
        {
            return null;
        }

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
            : new SuggestionMatch(start, caret - start, items);
    }
}
