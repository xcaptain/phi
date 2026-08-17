using System.Globalization;
using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using PhiCoding.Tools.Details;
using PhiCoding.Tui.Components.ToolCards;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui.Components;

/// <summary>
/// Renders an edit's old/new strings as a side-by-side diff using a single-row
/// <see cref="Grid"/> with two equal-width (<c>Star</c>) columns. The left
/// cell stacks all old lines; the right cell stacks all new lines. Every
/// line carries its own 1-based line number (dim) so the two columns stay
/// interpretable even when a change appears only on one side.
/// <para>
/// Line numbers are right-aligned to a shared width computed across ALL
/// edits in the <see cref="EditDetails"/> (and both columns), so every
/// block aligns its <c>│</c> separator at the same column even when the
/// blocks' line numbers have different digit counts. DiffPlex's
/// <see cref="ChangeType.Imaginary"/> padding rows (which have no real
/// line number) render as blank cells of that same width — so the
/// <c>│</c> separator stays vertically aligned on every row.
/// </para>
/// <para>
/// Each <see cref="EditOpDetails.FirstLine"/> shifts DiffPlex's local
/// (1-based per slice) line numbers into the file's actual line numbers
/// so the diff is anchored at its real position. <c>Wrap = true</c> on the
/// underlying XenoAtom <see cref="Markup"/> lets long lines wrap inside
/// the transcript card without producing a horizontal scrollbar.
/// </para>
/// <para>
/// Deleted lines are red, inserted lines green, unchanged lines plain.
/// <see cref="ChangeType.Modified"/> pairs read as red on the left and green
/// on the right.
/// </para>
/// </summary>
public static class SideBySideDiff
{
    /// <summary>Separator between the line number and the text within a cell.</summary>
    internal const string NumberSeparator = " │ ";

    public static Visual Build(EditDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        // One side-by-side Grid per applied edit. The diff models are built
        // up front so the line-number width can be shared across ALL edits:
        // every block then uses the same number-column width, so blocks
        // whose line numbers have different digit counts (e.g. 9 vs 100)
        // still align their "│" separators and text columns. A single edit
        // returns the Grid directly; multiple edits stack as one Grid per
        // edit so the user sees each change block in its own left/right pair.
        var models = details.Edits.Select(BuildDiffModel).ToList();
        var maxLineNoWidth = models
            .SelectMany(m => m.OldText.Lines.Concat(m.NewText.Lines))
            .Where(p => p.Position.HasValue)
            .Select(p => p.Position!.Value.ToString(CultureInfo.InvariantCulture).Length)
            .DefaultIfEmpty(1)
            .Max();
        var grids = models
            .Select(m => BuildEditGrid(m, maxLineNoWidth))
            .ToList();
        if (grids.Count == 1) return grids[0];

        return new VStack([.. grids]).Spacing(1);
    }

    private static SideBySideDiffModel BuildDiffModel(EditOpDetails op)
    {
        var model = new SideBySideDiffBuilder(DiffPlex.Differ.Instance)
            .BuildDiffModel(op.OldText, op.NewText);

        // Shift DiffPlex's per-slice line numbers (1-based) into the file's
        // actual line numbers so the rendered diff lines up with the
        // surrounding transcript / file explorer.
        var offset = op.FirstLine - 1;
        foreach (var piece in model.OldText.Lines)
            if (piece.Position.HasValue) piece.Position += offset;
        foreach (var piece in model.NewText.Lines)
            if (piece.Position.HasValue) piece.Position += offset;
        return model;
    }

    private static Grid BuildEditGrid(SideBySideDiffModel model, int maxLineNoWidth)
    {
        var oldLines = model.OldText.Lines;
        var newLines = model.NewText.Lines;

        var left = BuildColumnMarkup(oldLines, isLeft: true, maxLineNoWidth);
        var right = BuildColumnMarkup(newLines, isLeft: false, maxLineNoWidth);

        var grid = new Grid
        {
            ColumnGap = 2,
        };
        grid.Columns(
                new ColumnDefinition { Width = new GridLength(GridUnitType.Star, 1) },
                new ColumnDefinition { Width = new GridLength(GridUnitType.Star, 1) });
        grid.Cell(left, row: 0, column: 0);
        grid.Cell(right, row: 0, column: 1);
        return grid;
    }

    /// <summary>
    /// Builds one column's multi-line Markup: each line is the dim line
    /// number plus the change-colored text.
    /// </summary>
    private static Markup BuildColumnMarkup(
        List<DiffPiece> pieces, bool isLeft, int maxLineNoWidth)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pieces.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(BuildLine(pieces[i], SideTypeFor(pieces[i].Type, isLeft), maxLineNoWidth));
        }
        return new Markup(sb.ToString())
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Start,
            Wrap = true,
        };
    }

    private static string BuildLine(DiffPiece piece, SideType side, int maxLineNoWidth)
    {
        var text = piece.Text ?? "";
        // Right-align real line numbers; imaginary padding rows get a
        // same-width blank so the separator column stays aligned.
        var lineNo = piece.Position.HasValue
            ? piece.Position.Value.ToString(CultureInfo.InvariantCulture).PadLeft(maxLineNoWidth)
            : new string(' ', maxLineNoWidth);
        var marker = side switch
        {
            SideType.Removed => "- ",
            SideType.Added => "+ ",
            _ => "  ",
        };
        var textColor = side switch
        {
            SideType.Removed => "red",
            SideType.Added => "green",
            _ => "",
        };

        var numberPart = $"{lineNo}{NumberSeparator}";
        var body = $"{marker}{text}";
        return textColor.Length == 0
            ? $"[dim]{ToolCardBase.Escape(numberPart)}[/]{ToolCardBase.Escape(body)}"
            : $"[dim]{ToolCardBase.Escape(numberPart)}[/][{textColor}]{ToolCardBase.Escape(body)}[/]";
    }

    /// <summary>
    /// Maps a DiffPlex change type to a side-aware render type. A
    /// <see cref="ChangeType.Modified"/> pair appears in BOTH columns, so the
    /// side decides the color: removed-looking on the left, added-looking on
    /// the right.
    /// </summary>
    private static SideType SideTypeFor(ChangeType type, bool isLeft) => type switch
    {
        ChangeType.Deleted => SideType.Removed,
        ChangeType.Inserted => SideType.Added,
        ChangeType.Modified => isLeft ? SideType.Removed : SideType.Added,
        _ => SideType.Context,
    };

    private enum SideType
    {
        Context,
        Removed,
        Added,
    }
}