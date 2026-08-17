using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhiCoding.Avalonia.Components;
using PhiCoding.Tools.Details;
using TextBlock = global::Avalonia.Controls.TextBlock;

namespace PhiCoding.Avalonia.Tests;

/// <summary>
/// <see cref="SideBySideDiff"/>: mirrors the TUI's contract — every row
/// carries a line number, deleted lines are danger-colored on the left
/// (and absent on the right), inserted lines are success-colored on the
/// right (and absent on the left), modified pairs paint danger on the
/// left + success on the right, and line numbers right-align to a shared
/// width across both columns so the "│" separator stays vertical.
/// <para>
/// Each row is a 3-column Grid (line number | marker | text). The text
/// column is the only one allowed to wrap, so long lines fold inside
/// the card without producing a horizontal scrollbar.
/// </para>
/// </summary>
[NotInParallel("Avalonia-UI")]
public class SideBySideDiffTests
{
    private static EditDetails Edit(string oldText, string newText, int firstLine = 1) =>
        new("/tmp/a.cs",
            [new EditOpDetails(oldText, newText, firstLine)],
            Diff: "",
            Patch: "");

    private static Grid BuildSingle(string oldText, string newText, int firstLine = 1) =>
        (Grid)SideBySideDiff.Build(Edit(oldText, newText, firstLine));

    /// <summary>Walks a column StackPanel to one of its row Grids.</summary>
    private static Grid RowAt(StackPanel column, int index) =>
        (Grid)column.Children[index];

    /// <summary>Returns the number, marker, and text cells of one row
    /// (the vertical separator sits at index 1 between them).</summary>
    private static (TextBlock number, TextBlock marker, TextBlock text) RowCells(Grid row)
    {
        var number = (TextBlock)row.Children[0];
        var marker = (TextBlock)row.Children[2];
        var text = (TextBlock)row.Children[3];
        return (number, marker, text);
    }

    /// <summary>Pulls the left / right StackPanel from a two-column diff Grid.</summary>
    private static (StackPanel left, StackPanel right) Columns(Grid grid)
    {
        var left = (StackPanel)grid.Children[0];
        var right = (StackPanel)grid.Children[1];
        return (left, right);
    }

    /// <summary>The vertical separator Border at index 1 of a row.</summary>
    private static Border SeparatorOf(Grid row) => (Border)row.Children[1];

    [Test]
    public async Task Build_ReturnsTwoEqualStarColumns()
    {
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle("a\nb", "a\nb");

        await Assert.That(grid.ColumnDefinitions.Count).IsEqualTo(2);
        await Assert.That(grid.ColumnDefinitions[0].Width).IsEqualTo(new GridLength(1, GridUnitType.Star));
        await Assert.That(grid.ColumnDefinitions[1].Width).IsEqualTo(new GridLength(1, GridUnitType.Star));
    }

    [Test]
    public async Task Build_SingleLineChange_LeftDangerRightSuccess()
    {
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle(
            "line 1\nold line 2\nline 3",
            "line 1\nnew line 2\nline 3");
        var (left, right) = Columns(grid);

        // The middle row carries the diff: left = danger color, right = success.
        // (Row 0 is "line 1" context; row 2 is "line 3" context.)
        var (_, _, leftText) = RowCells(RowAt(left, 1));
        var (_, _, rightText) = RowCells(RowAt(right, 1));
        await Assert.That(leftText.Text).IsEqualTo("old line 2");
        await Assert.That(leftText.Foreground).IsEqualTo(AvaloniaTheme.Danger);
        await Assert.That(rightText.Text).IsEqualTo("new line 2");
        await Assert.That(rightText.Foreground).IsEqualTo(AvaloniaTheme.Success);

        // The other side must not carry the counterpart text.
        await Assert.That(rightText.Text).DoesNotContain("old line 2");
        await Assert.That(leftText.Text).DoesNotContain("new line 2");
    }

    [Test]
    public async Task Build_InsertedLine_RightColumnShowsIt_LeftHasImaginaryBlank()
    {
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle("a\nb", "a\nb\nc");
        var (left, right) = Columns(grid);

        // Both columns carry the same number of rows so the "│"
        // separator stays vertically aligned. DiffPlex pads the shorter
        // side with Imaginary rows on the left (no text, blank line
        // number); the right's last row is the inserted line.
        await Assert.That(left.Children.Count).IsEqualTo(right.Children.Count);

        var (_, _, rightText) = RowCells(RowAt(right, right.Children.Count - 1));
        await Assert.That(rightText.Text).IsEqualTo("c");
        await Assert.That(rightText.Foreground).IsEqualTo(AvaloniaTheme.Success);

        // The matching row on the left has no text (Imaginary).
        var (_, _, leftText) = RowCells(RowAt(left, left.Children.Count - 1));
        await Assert.That(leftText.Text).IsEqualTo("");
    }

    [Test]
    public async Task Build_DeletedLine_LeftColumnShowsItInDanger()
    {
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle("a\nb\nc", "a\nc");
        var (left, _) = Columns(grid);

        var (_, _, leftText) = RowCells(RowAt(left, 1));
        await Assert.That(leftText.Text).IsEqualTo("b");
        await Assert.That(leftText.Foreground).IsEqualTo(AvaloniaTheme.Danger);
    }

    [Test]
    public async Task Build_LineNumbers_AreOneBasedPerSide()
    {
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle("l1\nl2\nl3", "l1\nl2\nl3");
        var (left, _) = Columns(grid);

        var (n0, _, _) = RowCells(RowAt(left, 0));
        var (n1, _, _) = RowCells(RowAt(left, 1));
        var (n2, _, _) = RowCells(RowAt(left, 2));
        await Assert.That(n0.Text).IsEqualTo("1");
        await Assert.That(n1.Text).IsEqualTo("2");
        await Assert.That(n2.Text).IsEqualTo("3");
    }

    [Test]
    public async Task Build_EmptyStrings_NoCrash()
    {
        AvaloniaTestHost.EnsureInitialized();
        // Must not throw on empty diffs.
        var grid = BuildSingle("", "");
        await Assert.That(grid).IsNotNull();
    }

    [Test]
    public async Task Build_NoChanges_AllContextRowsAreDefaultColor()
    {
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle("same", "same");
        var (left, _) = Columns(grid);

        var (_, _, text) = RowCells(RowAt(left, 0));
        await Assert.That(text.Foreground).IsNull();
    }

    [Test]
    public async Task Build_Delete2Add4_LineNumbersAlignAcrossColumns()
    {
        // Regression: with 2 deletions + 4 insertions DiffPlex pads the
        // shorter side with Imaginary rows. Every row's line-number prefix
        // must be the same width on both columns so the "│" stays aligned.
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle(
            "l1\nl2\nl3\nl4\nl5\noldA\noldB\nl6\nl7",
            "l1\nl2\nl3\nl4\nl5\nnewA\nnewB\nnewC\nnewD\nl6\nl7");
        var (left, right) = Columns(grid);

        // Same number of rows on both sides (Imaginary padding).
        await Assert.That(left.Children.Count).IsEqualTo(right.Children.Count);

        // Every row's number prefix has the same width on both sides.
        for (var i = 0; i < left.Children.Count; i++)
        {
            var (ln, _, _) = RowCells(RowAt(left, i));
            var (rn, _, _) = RowCells(RowAt(right, i));
            await Assert.That(rn.Text!.Length).IsEqualTo(ln.Text!.Length);
        }
    }

    [Test]
    public async Task Build_TextBlockInRow_WrapsWhenContentExceedsColumn()
    {
        // Regression: long lines used to push the column wider than the
        // viewport and produce a horizontal scrollbar. The text block
        // must wrap so the row stays inside its column.
        AvaloniaTestHost.EnsureInitialized();
        var longLine = new string('x', 200);
        var grid = BuildSingle($"ctx\n{longLine}\nend", $"ctx\n{longLine}\nend");
        var (left, _) = Columns(grid);

        var (_, _, middleText) = RowCells(RowAt(left, 1));
        await Assert.That(middleText.TextWrapping).IsEqualTo(TextWrapping.Wrap);
    }

    [Test]
    public async Task Build_WrappedRow_SeparatorStretchesFullHeight()
    {
        // Regression: the "│" glyph used to be glued to the line number, so
        // a wrapped continuation line left a gap in the vertical separator.
        // The separator is now a 1-px Border stretched to the row's full
        // height, so it stays continuous even when the text wraps onto
        // multiple rows (only the number / marker stop after the first).
        AvaloniaTestHost.EnsureInitialized();
        var longLine = new string('x', 200);
        var grid = BuildSingle($"ctx\n{longLine}\nend", $"ctx\n{longLine}\nend");
        var (left, _) = Columns(grid);

        var row = RowAt(left, 1);
        var separator = SeparatorOf(row);
        await Assert.That(separator.Width).IsEqualTo(1);
        await Assert.That(separator.VerticalAlignment).IsEqualTo(VerticalAlignment.Stretch);
    }

    [Test]
    public async Task Build_MultipleEdits_StacksOneGridPerEdit()
    {
        AvaloniaTestHost.EnsureInitialized();
        var details = new EditDetails(
            "/tmp/a.cs",
            [
                new EditOpDetails("oldA", "newA", 1),
                new EditOpDetails("oldB", "newB", 10),
            ],
            Diff: "",
            Patch: "");

        var visual = SideBySideDiff.Build(details);
        var stack = (StackPanel)visual;
        await Assert.That(stack.Children.Count).IsEqualTo(2);
        await Assert.That(stack.Children[0]).IsTypeOf<Grid>();
        await Assert.That(stack.Children[1]).IsTypeOf<Grid>();
    }

    [Test]
    public async Task Build_SingleEdit_ReturnsGridDirectly()
    {
        AvaloniaTestHost.EnsureInitialized();
        var details = new EditDetails(
            "/tmp/a.cs",
            [new EditOpDetails("oldA", "newA", 1)],
            Diff: "",
            Patch: "");

        var visual = SideBySideDiff.Build(details);
        await Assert.That(visual).IsTypeOf<Grid>();
    }

    [Test]
    public async Task Build_FirstLineOffset_AnchorsNumbersAtFileLine()
    {
        // firstLine=5 means the slice starts at file line 5. The diff's
        // local 1-based line numbers (1..2) should render as 5..6 so the
        // user can see where in the file the change is. ("ctx" stays
        // unchanged, "A"→"B" is a Modified pair shown in both columns.)
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle("ctx\nA", "ctx\nB", firstLine: 5);
        var (left, right) = Columns(grid);

        var (ln0, _, _) = RowCells(RowAt(left, 0));
        var (ln1, _, _) = RowCells(RowAt(left, 1));
        await Assert.That(ln0.Text).IsEqualTo("5");
        await Assert.That(ln1.Text).IsEqualTo("6");  // Modified A

        var (rn0, _, _) = RowCells(RowAt(right, 0));
        var (rn1, _, _) = RowCells(RowAt(right, 1));
        await Assert.That(rn0.Text).IsEqualTo("5");
        await Assert.That(rn1.Text).IsEqualTo("6");  // Modified B
    }

    [Test]
    public async Task Build_MultipleEdits_EachAnchorsAtItsOwnFirstLine()
    {
        // First edit at file line 5, second at file line 35. Each side
        // should anchor at its own anchor, not at 1 — so the rendered
        // numbers reflect the actual file positions, not local slice
        // positions.
        AvaloniaTestHost.EnsureInitialized();
        var details = new EditDetails(
            "/tmp/a.cs",
            [
                new EditOpDetails("A1", "B1", 5),
                new EditOpDetails("A2", "B2", 35),
            ],
            Diff: "",
            Patch: "");

        var visual = SideBySideDiff.Build(details);
        var stack = (StackPanel)visual;

        var firstGrid = (Grid)stack.Children[0];
        var (ln0, _, _) = RowCells(RowAt((StackPanel)firstGrid.Children[0], 0));
        await Assert.That(ln0.Text).IsEqualTo(" 5");

        var secondGrid = (Grid)stack.Children[1];
        var (ln35, _, _) = RowCells(RowAt((StackPanel)secondGrid.Children[0], 0));
        await Assert.That(ln35.Text).IsEqualTo("35");
    }

    [Test]
    public async Task Build_MultipleEdits_ShareLineNumberWidthAcrossBlocks()
    {
        // Regression: each block used to compute its own number-column
        // width, so a block anchored near line 9 and another near line 100
        // rendered their "│" separators at different columns. The number
        // width must be shared across ALL edits so every block's separator
        // and text column align.
        AvaloniaTestHost.EnsureInitialized();
        var details = new EditDetails(
            "/tmp/a.cs",
            [
                new EditOpDetails("oldA", "newA", 9),
                new EditOpDetails("oldB", "newB", 100),
            ],
            Diff: "",
            Patch: "");

        var visual = SideBySideDiff.Build(details);
        var stack = (StackPanel)visual;

        // Block 1's short numbers are right-padded to the shared width
        // (3 digits, from block 2's "100"), so the separator lands at the
        // same column as in block 2.
        var firstGrid = (Grid)stack.Children[0];
        var (n9, _, _) = RowCells(RowAt((StackPanel)firstGrid.Children[0], 0));
        await Assert.That(n9.Text).IsEqualTo("  9");

        var secondGrid = (Grid)stack.Children[1];
        var (n100, _, _) = RowCells(RowAt((StackPanel)secondGrid.Children[0], 0));
        await Assert.That(n100.Text).IsEqualTo("100");

        await Assert.That(n100.Text!.Length).IsEqualTo(n9.Text!.Length);
    }
}
