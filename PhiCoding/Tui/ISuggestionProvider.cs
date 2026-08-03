namespace PhiCoding.Tui;

/// <summary>
/// Supplies context-aware autocomplete suggestions for the prompt editor.
/// Implementations inspect the current input and caret, and return a
/// replacement span plus candidate items — or null to show nothing. The
/// slash-command provider is the first consumer; a future <c>@</c> file
/// picker is another.
/// </summary>
public interface ISuggestionProvider
{
    /// <summary>
    /// Returns suggestions for <paramref name="text"/> at <paramref name="caret"/>,
    /// or null when the input doesn't trigger this provider.
    /// </summary>
    SuggestionMatch? GetSuggestion(ReadOnlySpan<char> text, int caret);
}
