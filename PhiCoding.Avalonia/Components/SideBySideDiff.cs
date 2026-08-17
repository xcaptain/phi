using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using PhiCoding.Tools.Details;

namespace PhiCoding.Avalonia.Components;

/// <summary>
/// Renders an edit's old/new strings as a side-by-side diff: a single-row
/// <see cref="Grid"/> with two equal-width (<c>Star</c>) columns. The left
/// cell stacks all old lines; the right cell stacks all new lines. Every
/// line carries its own 1-based line number so the two columns stay
/// interpretable even when a change appears only on one side.
/// <para>
/// Line numbers are right-aligned to a shared width computed across BOTH
/// columns, and DiffPlex's <see cref="ChangeType.Imaginary"/> padding rows
/// (which have no real line number) render as blank cells of the same
/// width — so the <c>│</c> separator stays vertically aligned on every
/// row.
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
    /// <summary>Separator between the line number and the text within a cell.</summary>
    private const string NumberSeparator = " │ ";

    /// <summary>Builds the diff for an <see cref="EditDetails"/>: a single
    /// Grid for one edit, a vertical stack of Grids for multiple edits.</summary>
    public static Control Build(EditDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var grids = details.Edits.Select(BuildEditGrid).ToList();
        if (grids.Count == 1) return grids[0];

        var stack = new StackPanel { Spacing = 8 };
        foreach (var g in grids) stack.Children.Add(g);
        return stack;
    }

    private static Grid BuildEditGrid(EditOpDetails op)
    {
        var model = new SideBySideDiffBuilder(DiffPlex.Differ.Instance)
            .BuildDiffModel(op.OldText, op.NewText);

        var oldLines = model.OldText.Lines;
        var newLines = model.NewText.Lines;

        // Shared line-number width across both columns keeps the "│"
        // separator aligned on every row.
        var maxLineNoWidth = oldLines
            .Concat(newLines)
            .Where(p => p.Position > 0)
            .Select(p => p.Position!.Value.ToString(CultureInfo.InvariantCulture).Length)
            .DefaultIfEmpty(1)
            .Max();

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

    /// <summary>One diff row: dim line number, separator, change-colored text.</summary>
    private static DockPanel BuildLine(DiffPiece piece, SideType side, int maxLineNoWidth)
    {
        var text = piece.Text ?? "";
        // Right-align real line numbers; imaginary padding rows get a
        // same-width blank so the separator column stays aligned.
        var lineNo = piece.Position is { } pos && pos > 0
            ? pos.ToString(CultureInfo.InvariantCulture).PadLeft(maxLineNoWidth)
            : new string(' ', maxLineNoWidth);

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

        var numberBlock = new TextBlock
        {
            Text = lineNo + NumberSeparator,
            Foreground = AvaloniaTheme.TextSecondary,
            FontFamily = AvaloniaTheme.MonoFontFamily,
        };
        var textBlock = new TextBlock
        {
            Text = marker + text,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = AvaloniaTheme.MonoFontFamily,
            Foreground = foreground,
        };

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(numberBlock, Dock.Left);
        row.Children.Add(numberBlock);
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
