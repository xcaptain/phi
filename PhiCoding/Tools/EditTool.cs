using System.ComponentModel;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using DiffPlex.Renderer;
using PhiAgent;
using PhiCoding.Tools.Details;

namespace PhiCoding.Tools;

public sealed record EditOp
{
    [Description("Exact text to find in the original file (must be unique and non-overlapping)")]
    public required string OldText { get; init; }

    [Description("Replacement text")]
    public required string NewText { get; init; }
}

public sealed record EditArgs
{
    [Description("File path to edit")]
    public required string Path { get; init; }

    [Description("One or more targeted replacements. Each oldText must match exactly once in the original file; edits must not overlap.")]
    public required EditOp[] Edits { get; init; }
}

public sealed partial class EditTool : TypedTool<EditArgs>
{
    public override string Name => "edit";

    public override string Description =>
        "Edit a single file using exact text replacement. Accepts one or more edits[]; " +
        "every edits[].oldText must match a unique, non-overlapping region of the ORIGINAL file " +
        "(not after earlier edits are applied). When changing multiple separate locations, use " +
        "one call with multiple edits[] entries instead of multiple edit calls.";

    public override async Task<ToolResult> ExecuteTypedAsync(EditArgs args, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(args.Path))
                return new ToolResult(
                    [new TextBlock($"File not found: {args.Path}")],
                    IsError: true);

            if (args.Edits.Length == 0)
                return new ToolResult(
                    [new TextBlock("edits must contain at least one replacement")],
                    IsError: true);

            // Read raw bytes: File.ReadAllText would strip the UTF-8 BOM
            // (encoding auto-detection), losing it before we can preserve it.
            var rawBytes = await File.ReadAllBytesAsync(args.Path, cancellationToken);
            var (bom, content) = StripBom(rawBytes);
            var originalEnding = DetectLineEnding(content);
            var normalized = NormalizeToLf(content);

            var matches = new List<(int Start, int End, EditOp Edit)>();
            for (var i = 0; i < args.Edits.Length; i++)
            {
                var edit = args.Edits[i];
                var oldText = NormalizeToLf(edit.OldText);
                if (oldText.Length == 0)
                    return new ToolResult(
                        [new TextBlock($"edits[{i}].oldText must not be empty")],
                        IsError: true);

                var occurrences = CountOccurrences(normalized, oldText);
                if (occurrences == 0)
                    return new ToolResult(
                        [new TextBlock($"edits[{i}].oldText not found in {args.Path}")],
                        IsError: true);
                if (occurrences > 1)
                    return new ToolResult(
                        [new TextBlock($"edits[{i}].oldText appears {occurrences} times in {args.Path} — must be unique")],
                        IsError: true);

                var start = normalized.IndexOf(oldText, StringComparison.Ordinal);
                matches.Add((start, start + oldText.Length, edit));
            }

            // All edits validated against the original file; reject overlaps.
            var sorted = matches.OrderBy(m => m.Start).ToList();
            for (var i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Start < sorted[i - 1].End)
                    return new ToolResult(
                        [new TextBlock("edits must not overlap")],
                        IsError: true);
            }

            // Apply from the tail so earlier spans stay valid.
            var newNormalized = normalized;
            foreach (var (start, end, edit) in sorted.OrderByDescending(m => m.Start))
            {
                newNormalized = newNormalized[..start]
                    + NormalizeToLf(edit.NewText)
                    + newNormalized[end..];
            }

            if (newNormalized == normalized)
                return new ToolResult(
                    [new TextBlock($"No change made to {args.Path}")],
                    IsError: true);

            var finalContent = bom + RestoreLineEndings(newNormalized, originalEnding);
            // Write with an explicit UTF-8 encoding that does NOT add a BOM
            // by itself; the bom string (when present) supplies the EF BB BF
            // prefix so a BOM-less file stays BOM-less.
            var finalBytes = new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false)
                .GetBytes(finalContent);
            await File.WriteAllBytesAsync(args.Path, finalBytes, cancellationToken);

            var (diffText, firstChangedLine) = GenerateDiffString(normalized, newNormalized);
            var patch = UnidiffRenderer.GenerateUnidiff(
                oldText: normalized,
                newText: newNormalized,
                oldFileName: args.Path,
                newFileName: args.Path);

            var editOps = matches
                .OrderBy(m => m.Start)
                .Select(m => new EditOpDetails(
                    OldText: NormalizeToLf(m.Edit.OldText),
                    NewText: NormalizeToLf(m.Edit.NewText)))
                .ToList();

            var details = new EditDetails(
                Path: args.Path,
                Edits: editOps,
                Diff: diffText,
                Patch: patch,
                FirstChangedLine: firstChangedLine);

            var opCount = matches.Count;
            return new ToolResult(
                [new TextBlock($"Edited {args.Path} ({opCount} block(s), {args.Edits.Length} edit(s))")],
                Details: ToolDetails.Node(details));
        }
        catch (UnauthorizedAccessException)
        {
            return new ToolResult(
                [new TextBlock($"Permission denied editing: {args.Path}")],
                IsError: true);
        }
        catch (Exception ex)
        {
            return new ToolResult(
                [new TextBlock($"Error editing {args.Path}: {ex.Message}")],
                IsError: true);
        }
    }

    private static int CountOccurrences(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 0;
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    // ──────── BOM / line-ending normalization (mirrors tau) ────────

    /// <summary>
    /// Splits a UTF-8 byte-order mark (if present) from the decoded content.
    /// Returns the BOM as a string prefix to re-prepend on write, and the
    /// content with the BOM removed.
    /// </summary>
    private static (string Bom, string Content) StripBom(byte[] bytes)
    {
        var hasBom = bytes.Length >= 3
            && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var offset = hasBom ? 3 : 0;
        var content = System.Text.Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        return (hasBom ? "\uFEFF" : "", content);
    }

    private static string DetectLineEnding(string content)
    {
        var crlf = content.IndexOf("\r\n", StringComparison.Ordinal);
        var lf = content.IndexOf('\n');
        if (lf < 0 || crlf < 0) return "\n";
        return crlf < lf ? "\r\n" : "\n";
    }

    private static string NormalizeToLf(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string RestoreLineEndings(string text, string ending) =>
        ending == "\r\n" ? text.Replace("\n", "\r\n") : text;

    /// <summary>
    /// Produces a line-based diff string and the first changed line number.
    /// Uses DiffPlex's inline diff builder (line chunker).
    /// </summary>
    private static (string Diff, int? FirstChangedLine) GenerateDiffString(string oldText, string newText)
    {
        var model = InlineDiffBuilder.Diff(
            DiffPlex.Differ.Instance,
            oldText,
            newText,
            ignoreWhiteSpace: false,
            ignoreCase: false);

        var lines = new List<string>();
        int? firstChanged = null;
        var newLineNo = 0;
        foreach (var piece in model.Lines)
        {
            var text = piece.Text ?? "";
            switch (piece.Type)
            {
                case ChangeType.Unchanged:
                    newLineNo++;
                    lines.Add("  " + text);
                    break;
                case ChangeType.Inserted:
                    newLineNo++;
                    firstChanged ??= newLineNo;
                    lines.Add("+ " + text);
                    break;
                case ChangeType.Deleted:
                    firstChanged ??= newLineNo + 1;
                    lines.Add("- " + text);
                    break;
                case ChangeType.Modified:
                    newLineNo++;
                    firstChanged ??= newLineNo;
                    lines.Add("- " + text);
                    break;
                default:
                    lines.Add("  " + text);
                    break;
            }
        }
        return (string.Join("\n", lines), firstChanged);
    }
}