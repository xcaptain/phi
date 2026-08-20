namespace Phi.Prompt;

/// <summary>One selectable suggestion shown in the suggestion strip.</summary>
/// <param name="Label">Text displayed as the primary label.</param>
/// <param name="Description">One-line description shown beside the label.</param>
/// <param name="Replacement">Text inserted when the suggestion is accepted.</param>
public sealed record SuggestionItem(string Label, string Description, string Replacement);

/// <summary>
/// A provider's answer for the current input: a span to replace and the
/// candidate items. Null means "no suggestions for this input".
/// </summary>
public sealed record SuggestionMatch(
    int ReplaceStart,
    int ReplaceLength,
    IReadOnlyList<SuggestionItem> Items);
