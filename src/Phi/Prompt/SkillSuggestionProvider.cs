using Phi.Prompts;

namespace Phi.Prompt;

/// <summary>
/// <see cref="ISuggestionProvider"/> that completes <c>/skill:NAME</c>.
/// Triggers when the token ending at the caret starts with <c>/skill</c>
/// (so typing <c>/skill</c> lists every skill, and <c>/skill:dot</c>
/// filters to the ones whose name starts with <c>dot</c>). The replacement
/// span covers only the current token, so accepting a suggestion replaces
/// just that token with <c>/skill:&lt;name&gt;</c>.
/// </summary>
public sealed class SkillSuggestionProvider(IReadOnlyList<SkillDescriptor> skills)
    : ISuggestionProvider
{
    private const string SkillPrefix = "/skill";

    public SuggestionMatch? GetSuggestion(ReadOnlySpan<char> text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var start = caret;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        var prefix = text[start..caret];
        if (prefix.Length == 0
            || !prefix.StartsWith(SkillPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // The query is whatever follows "/skill" (optionally a ":"), so
        // "/skill" matches everything and "/skill:dot" filters to names
        // starting with "dot". Matching is case-insensitive.
        var raw = prefix[SkillPrefix.Length..].ToString();
        var query = raw.Length > 0 && raw[0] == ':' ? raw[1..] : raw;

        List<SuggestionItem>? items = null;
        foreach (var skill in skills)
        {
            var name = skill.Name;
            if (!name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                continue;
            (items ??= new List<SuggestionItem>(skills.Count)).Add(
                new SuggestionItem(
                    name,
                    skill.Description,
                    $"{SkillPrefix}:{name}"));
        }

        return items is null || items.Count == 0
            ? null
            : new SuggestionMatch(start, caret - start, items);
    }
}
