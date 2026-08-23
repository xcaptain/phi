using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Phi.Extensions.CodingPack.Tools.Details;

namespace Phi.Avalonia.Components;

/// <summary>
/// Renders an edit's old/new strings as a side-by-side diff in the
/// GitHub style: a single-row <see cref="Grid"/> with two equal-width
/// (<c>Star</c>) columns. Each row inside a column is itself a 4-column
/// grid — line number (right-aligned, dim) | continuous vertical
/// separator | marker (- / + / blank) | text (wraps). Wrapping long
/// lines no longer produces a horizontal scrollbar; the number /
/// separator / marker columns stay short so wrapped second lines keep
/// their left margin aligned with the rest of the column, and the
/// separator stretches to the full row height so it stays continuous
/// across a wrapped line.
/// <para>
/// Line numbers are offset by <see cref="EditOpDetails.FirstLine"/> so
/// each edit is anchored at its real file position even when multiple
/// edits exist in one <see cref="EditDetails"/>. The line-number width is
/// shared across ALL edits so every block aligns its separator at the
/// same column. DiffPlex's
/// <see cref="ChangeType.Imaginary"/> padding rows (no real line number)
/// render as blank cells of the same width so the separator stays
/// vertically aligned on every row.
/// </para>
/// <para>
/// Deleted lines use <see cref="AvaloniaTheme.Danger"/>, inserted lines
/// <see cref="AvaloniaTheme.Success"/>, unchanged lines the default
/// foreground. <see cref="ChangeType.Modified"/> pairs read as
/// Danger-colored on the left and Success-colored on the right.
/// </para>
/// </summary>
public static class SideBySideDiff
{
    /// <summary>Builds the diff for an <see cref="EditDetails"/>: a single
    /// Grid for one edit, a vertical stack of Grids for multiple edits.</summary>
    public static Control Build(EditDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        // The diff models are built up front so the line-number width can
        // be shared across ALL edits: every block then uses the same
        // number-column width, so blocks whose line numbers have different
        // digit counts (e.g. 9 vs 100) still align their separators
        // and text columns.
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

        var stack = new StackPanel { Spacing = 8 };
        foreach (var g in grids) stack.Children.Add(g);
        return stack;
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

        var left = BuildColumn(oldLines, isLeft: true, maxLineNoWidth);
        var right = BuildColumn(newLines, isLeft: false, maxLineNoWidth);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Stacks the per-line rows for one side of the diff.</summary>
    private static StackPanel BuildColumn(
        List<DiffPiece> pieces, bool isLeft, int maxLineNoWidth)
    {
        var column = new StackPanel { Spacing = 0 };
        foreach (var piece in pieces)
            column.Children.Add(BuildLine(piece, SideTypeFor(piece.Type, isLeft), maxLineNoWidth));
        return column;
    }

    /// <summary>
    /// One diff row: dim right-aligned line number, a continuous vertical
    /// separator, a fixed-width marker (- / + / blank), and a wrapping mono
    /// text block. The four children sit in a single Grid with columns
    /// <c>Auto,Auto,Auto,*</c> so the number + separator + marker columns
    /// stay short and aligned, and the text column absorbs the rest of the
    /// width and is the only one that wraps.
    /// <para>
    /// The separator is a 1-px <see cref="Border"/> stretched to the row's
    /// full height rather than a "│" glyph glued to the number. When a long
    /// line wraps onto multiple rows the vertical line therefore stays
    /// continuous; only the line number / marker stop on the first row,
    /// which is the expected behavior for a wrapped line.
    /// </para>
    /// </summary>
    private static Grid BuildLine(DiffPiece piece, SideType side, int maxLineNoWidth)
    {
        var text = piece.Text ?? "";
        var marker = side switch
        {
            SideType.Removed => "- ",
            SideType.Added => "+ ",
            _ => "  ",
        };
        var foreground = side switch
        {
            SideType.Removed => AvaloniaTheme.Danger,
            SideType.Added => AvaloniaTheme.Success,
            _ => (IBrush?)null,
        };

        // Right-align real line numbers; imaginary padding rows get a
        // same-width blank so the number column stays aligned.
        var lineNoText = piece.Position.HasValue
            ? piece.Position.Value.ToString(CultureInfo.InvariantCulture).PadLeft(maxLineNoWidth)
            : new string(' ', maxLineNoWidth);

        var numberBlock = new TextBlock
        {
            Text = lineNoText,
            TextAlignment = TextAlignment.Right,
            FontFamily = AvaloniaTheme.MonoFontFamily,
            Foreground = AvaloniaTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var separator = new Border
        {
            Width = 1,
            Background = AvaloniaTheme.TextSecondary,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(6, 0, 6, 0),
        };
        var markerBlock = new TextBlock
        {
            Text = marker,
            FontFamily = AvaloniaTheme.MonoFontFamily,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var textBlock = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = AvaloniaTheme.MonoFontFamily,
            Foreground = foreground,
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*") };
        Grid.SetColumn(numberBlock, 0);
        Grid.SetColumn(separator, 1);
        Grid.SetColumn(markerBlock, 2);
        Grid.SetColumn(textBlock, 3);
        row.Children.Add(numberBlock);
        row.Children.Add(separator);
        row.Children.Add(markerBlock);
        row.Children.Add(textBlock);
        return row;
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
