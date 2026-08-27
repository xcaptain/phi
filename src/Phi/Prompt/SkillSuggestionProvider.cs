using Phi.Prompts;

namespace Phi.Prompt;

/// <summary>
/// <see cref="ISuggestionProvider"/> that completes <c>/skill:NAME</c>.
/// Triggers when the buffer starts with <c>/</c> on the first line
/// <em>and</em> the typed prefix begins with <c>/skill</c>. Typing
/// <c>/skill</c> lists every skill; typing <c>/skill:dot</c> filters
/// to the ones whose name starts with <c>dot</c>. The replacement
/// span covers only the leading-<c>/</c> command token, so accepting
/// a suggestion replaces just that token even when the user has
/// already typed arguments after it.
/// <para>
/// Like the other built-in providers, this only fires when the buffer
/// starts with <c>/</c> on the first line. A slash buried mid-sentence
/// like <c>"please /skill:foo"</c>, or a command typed on a
/// continuation line like <c>"hello\n/skill:dot"</c>, is ordinary text
/// and produces no suggestions. The strict "first-line slash" gate is
/// shared via <see cref="SuggestionTrigger.StartsWithSlashOnFirstLine"/>.
/// </para>
/// </summary>
public sealed class SkillSuggestionProvider(IReadOnlyList<SkillDescriptor> skills)
    : ISuggestionProvider
{
    private const string SkillPrefix = "/skill";

    public SuggestionMatch? GetSuggestion(ReadOnlySpan<char> text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);

        var asString = text.ToString();
        if (!SuggestionTrigger.StartsWithSlashOnFirstLine(asString, caret))
        {
            return null;
        }

        // The trigger guarantees the prefix starts with '/' and the
        // caret is on the first line, so we only need to bound the
        // prefix at the first whitespace (to drop trailing args) and
        // confirm it begins with "/skill".
        var tokenEnd = FindTokenEnd(asString, caret);
        var prefix = text[0..tokenEnd];
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
            : new SuggestionMatch(0, tokenEnd, items);
    }

    /// <summary>
    /// Returns the index just past the first whitespace between the buffer
    /// start and <paramref name="caret"/> (or <paramref name="caret"/>
    /// itself if no whitespace appears). Bounds the prefix to the leading
    /// command token. The newline guard is implicit: the trigger rejects
    /// inputs whose caret has crossed a newline.
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
