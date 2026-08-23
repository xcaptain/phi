namespace Phi.Extensions.CodingPack.Tools.Details;

/// <summary>
/// One applied edit's original and replacement text (LF-normalized), plus
/// the file line number where the first old line of this edit lives.
/// <see cref="FirstLine"/> lets the side-by-side diff renderer offset
/// DiffPlex's local (1-based per slice) line numbers into the actual
/// file line numbers so each edit is anchored at its real position even
/// when multiple edits exist in the same <see cref="EditDetails"/>.
/// </summary>
public sealed record EditOpDetails(string OldText, string NewText, int FirstLine = 1);

public sealed record EditDetails(
    string Path,
    IReadOnlyList<EditOpDetails> Edits,
    string Diff,
    string Patch);
