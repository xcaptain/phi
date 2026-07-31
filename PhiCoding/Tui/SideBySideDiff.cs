using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using PhiCoding.Tools.Details;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;

namespace PhiCoding.Tui;

/// <summary>
/// Renders an edit's old/new strings as a side-by-side diff using a single-row
/// <see cref="Grid"/> with two equal-width (<c>Star</c>) columns. The left
/// cell stacks all old lines; the right cell stacks all new lines. Every line
/// carries its own 1-based line number (dim) so the two columns stay
/// interpretable even when a change appears only on one side.
/// <para>
/// Line numbers are right-aligned to a shared width computed across BOTH
/// columns, and DiffPlex's <see cref="ChangeType.Imaginary"/> padding rows
/// (which have no real line number) render as blank cells of the same width —
/// so the <c>│</c> separator stays vertically aligned on every row.
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

        var model = new SideBySideDiffBuilder(DiffPlex.Differ.Instance)
            .BuildDiffModel(details.OldString, details.NewString);

        var oldLines = model.OldText.Lines;
        var newLines = model.NewText.Lines;

        // Shared line-number width across both columns keeps the "│"
        // separator aligned on every row.
        var maxLineNoWidth = oldLines
            .Concat(newLines)
            .Where(p => p.Position > 0)
            .Select(p => p.Position.ToString().Length)
            .DefaultIfEmpty(1)
            .Max();

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
        IReadOnlyList<DiffPiece> pieces, bool isLeft, int maxLineNoWidth)
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
        var lineNo = piece.Position > 0
            ? piece.Position.ToString().PadLeft(maxLineNoWidth)
            : new string(' ', maxLineNoWidth);
        var marker = side switch
        {
            SideType.Removed => "- ",
            SideType.Added => "+ ",
            _ => "",
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
            ? $"[dim]{ToolCardRenderer.Escape(numberPart)}[/]{ToolCardRenderer.Escape(body)}"
            : $"[dim]{ToolCardRenderer.Escape(numberPart)}[/][{textColor}]{ToolCardRenderer.Escape(body)}[/]";
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