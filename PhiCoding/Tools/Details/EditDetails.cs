namespace PhiCoding.Tools.Details;

/// <summary>One applied edit's original and replacement text (LF-normalized).</summary>
public sealed record EditOpDetails(string OldText, string NewText);

public sealed record EditDetails(
    string Path,
    IReadOnlyList<EditOpDetails> Edits,
    string Diff,
    string Patch,
    int? FirstChangedLine);