namespace PhiCoding.Tui;

public enum DiffLineKind
{
    Header,   // --- / +++
    Hunk,     // @@ ... @@
    Context,  // ' ' line
    Added,    // '+' line
    Removed,  // '-' line
    Note,     // "\ No newline at end of file"
}

public sealed record DiffLine(DiffLineKind Kind, string Text);

/// <summary>
/// Parses a unified diff (as produced by DiffPlex's <c>UnidiffRenderer</c>)
/// into a list of typed lines. Pure function — the renderer applies colors
/// per <see cref="DiffLineKind"/>.
/// </summary>
public static class DiffFormatter
{
    public static IReadOnlyList<DiffLine> Parse(string unidiff)
    {
        if (string.IsNullOrEmpty(unidiff)) return Array.Empty<DiffLine>();

        var lines = new List<DiffLine>();
        foreach (var raw in unidiff.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.Length == 0) continue;

            DiffLineKind kind;
            if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("+++", StringComparison.Ordinal))
                kind = DiffLineKind.Header;
            else if (line.StartsWith("@@", StringComparison.Ordinal) && line.EndsWith("@@", StringComparison.Ordinal))
                kind = DiffLineKind.Hunk;
            else if (line.StartsWith('\\'))
                kind = DiffLineKind.Note;
            else if (line[0] == '+')
                kind = DiffLineKind.Added;
            else if (line[0] == '-')
                kind = DiffLineKind.Removed;
            else if (line[0] == ' ')
                kind = DiffLineKind.Context;
            else
                kind = DiffLineKind.Context;

            lines.Add(new DiffLine(kind, line));
        }
        return lines;
    }
}
