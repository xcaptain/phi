using PhiCoding.Tools.Details;
using PhiCoding.Tui.Components;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tests;

[NotInParallel(TuiTestGroups.BindingManager)]
public class SideBySideDiffTests
{
    /// <summary>
    /// Builds a single-edit <see cref="EditDetails"/>. The optional
    /// <paramref name="firstLine"/> anchors the slice at its real file
    /// position; default 1 keeps the existing single-file tests
    /// (where the slice is the entire file) passing unchanged.
    /// </summary>
    private static EditDetails Edit(string oldText, string newText, int firstLine = 1) =>
        new("/tmp/a.cs",
            [new EditOpDetails(oldText, newText, firstLine)],
            Diff: "",
            Patch: "");

    private static Grid Build(string oldText, string newText, int firstLine = 1) =>
        (Grid)SideBySideDiff.Build(Edit(oldText, newText, firstLine))!;

    private static string LeftCellText(Grid grid) =>
        ((Markup)grid.Cells[0].Content!).Text!;

    private static string RightCellText(Grid grid) =>
        ((Markup)grid.Cells[1].Content!).Text!;

    [Test]
    public async Task Build_ReturnsSingleRowGrid_WithTwoEqualColumns()
    {
        var grid = Build("a\nb", "a\nb");

        await Assert.That(grid.Cells.Count).IsEqualTo(2);
        await Assert.That(grid.ColumnDefinitions.Count).IsEqualTo(2);
        await Assert.That(grid.ColumnDefinitions[0].Width.Type)
            .IsEqualTo(GridUnitType.Star);
        await Assert.That(grid.ColumnDefinitions[1].Width.Type)
            .IsEqualTo(GridUnitType.Star);
    }

    [Test]
    public async Task Build_SingleLineChange_RedLeftGreenRight()
    {
        var grid = Build("line 1\nold line 2\nline 3", "line 1\nnew line 2\nline 3");
        var left = LeftCellText(grid);
        var right = RightCellText(grid);

        // Both columns carry their own line numbers. Context lines carry
        // a 2-space marker so they align with the +/- marker column.
        await Assert.That(left).Contains("[dim]1 │ [/]  line 1");
        await Assert.That(right).Contains("[dim]1 │ [/]  line 1");
        // Old side red, new side green.
        await Assert.That(left).Contains("[red]- old line 2[/]");
        await Assert.That(right).Contains("[green]+ new line 2[/]");
        // New side must not show the old text.
        await Assert.That(right).DoesNotContain("old line 2");
        await Assert.That(left).DoesNotContain("new line 2");
    }

    [Test]
    public async Task Build_InsertedLine_LeftColumnOmitsIt_RightShowsGreen()
    {
        var grid = Build("a\nb", "a\nb\nc");
        var left = LeftCellText(grid);
        var right = RightCellText(grid);

        // Right side has 3 numbered lines, including the inserted green one.
        await Assert.That(right).Contains("[dim]3 │ [/]");
        await Assert.That(right).Contains("[green]+ c[/]");
        // Left side has only 2 lines (its own numbering).
        await Assert.That(left).DoesNotContain("3 │");
    }

    [Test]
    public async Task Build_DeletedLine_LeftShowsRed_RightOmitsIt()
    {
        var grid = Build("a\nb\nc", "a\nc");
        var left = LeftCellText(grid);
        var right = RightCellText(grid);

        await Assert.That(left).Contains("[red]- b[/]");
        await Assert.That(right).DoesNotContain("b");
    }

    [Test]
    public async Task Build_LineNumbers_AreOneBasedPerSide()
    {
        var grid = Build("l1\nl2\nl3", "l1\nl2\nl3");
        var left = LeftCellText(grid);

        await Assert.That(left).Contains("1 │ ");
        await Assert.That(left).Contains("2 │ ");
        await Assert.That(left).Contains("3 │ ");
    }

    [Test]
    public async Task Build_EmptyStrings_NoCrash()
    {
        var grid = Build("", "");
        var left = LeftCellText(grid);
        await Assert.That(left).IsEqualTo("");
    }

    [Test]
    public async Task Build_NoChanges_AllContext()
    {
        var grid = Build("same", "same");
        var left = LeftCellText(grid);
        await Assert.That(left).DoesNotContain("[red]");
        await Assert.That(left).DoesNotContain("[green]");
    }

    [Test]
    public async Task Build_Delete2Add4_LineNumbersAlignAcrossColumns()
    {
        // Regression: with 2 deletions + 4 insertions DiffPlex pads the
        // shorter side with Imaginary rows. Every row's line-number prefix
        // must be the same width on both columns so the "│" stays aligned,
        // and imaginary rows must render as blank cells of that same width
        // (not collapse to a narrower prefix).
        var grid = Build(
            "l1\nl2\nl3\nl4\nl5\noldA\noldB\nl6\nl7",
            "l1\nl2\nl3\nl4\nl5\nnewA\nnewB\nnewC\nnewD\nl6\nl7");
        var left = LeftCellText(grid);
        var right = RightCellText(grid);

        // Same number of rows on both sides.
        await Assert.That(left.Split('\n').Length).IsEqualTo(right.Split('\n').Length);

        // Every row's number prefix has the same width (2 digits here),
        // so the separator sits at the same column in both Markups.
        var leftRows = left.Split('\n');
        var rightRows = right.Split('\n');
        for (var i = 0; i < leftRows.Length; i++)
        {
            var lSep = leftRows[i].IndexOf('│');
            var rSep = rightRows[i].IndexOf('│');
            await Assert.That(rSep).IsEqualTo(lSep);
        }
    }

    [Test]
    public async Task Build_MultipleEdits_StacksOneGridPerEdit()
    {
        var details = new EditDetails(
            "/tmp/a.cs",
            [
                new EditOpDetails("oldA", "newA", 1),
                new EditOpDetails("oldB", "newB", 10),
            ],
            Diff: "",
            Patch: "");

        var visual = SideBySideDiff.Build(details);

        // Two edits → a VStack of two Grids.
        var stack = (VStack)visual;
        await Assert.That(stack.Children.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Build_SingleEdit_ReturnsGridDirectly()
    {
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
        var grid = Build("ctx\nA", "ctx\nB", firstLine: 5);
        var left = LeftCellText(grid);
        var right = RightCellText(grid);

        await Assert.That(left).Contains("[dim]5 │ [/]  ctx");
        await Assert.That(left).Contains("[dim]6 │ ");  // Modified A
        await Assert.That(right).Contains("[dim]5 │ [/]  ctx");
        await Assert.That(right).Contains("[dim]6 │ ");  // Modified B
    }

    [Test]
    public async Task Build_MultipleEdits_EachAnchorsAtItsOwnFirstLine()
    {
        // First edit at file line 5, second at file line 35. Each side
        // should anchor at its own anchor, not at 1 — so the rendered
        // numbers reflect the actual file positions, not local slice
        // positions.
        var details = new EditDetails(
            "/tmp/a.cs",
            [
                new EditOpDetails("A1", "B1", 5),
                new EditOpDetails("A2", "B2", 35),
            ],
            Diff: "",
            Patch: "");

        var visual = SideBySideDiff.Build(details);
        var stack = (VStack)visual;

        var firstGrid = (Grid)stack.Children[0];
        var firstLeft = LeftCellText(firstGrid);
        await Assert.That(firstLeft).Contains("[dim] 5 │ ");

        var secondGrid = (Grid)stack.Children[1];
        var secondLeft = LeftCellText(secondGrid);
        await Assert.That(secondLeft).Contains("[dim]35 │ ");
    }

    [Test]
    public async Task Build_MultipleEdits_ShareLineNumberWidthAcrossBlocks()
    {
        // Regression: each block used to compute its own number-column
        // width, so a block anchored near line 9 and another near line 100
        // rendered their "│" separators at different columns. With line
        // numbers present there is no need for the misalignment — the
        // number width must be shared across ALL edits so every block's
        // separator and text column align.
        var details = new EditDetails(
            "/tmp/a.cs",
            [
                new EditOpDetails("oldA", "newA", 9),
                new EditOpDetails("oldB", "newB", 100),
            ],
            Diff: "",
            Patch: "");

        var visual = SideBySideDiff.Build(details);
        var stack = (VStack)visual;

        // Block 1's short numbers are right-padded to the shared width
        // (3 digits, from block 2's "100"), so the separator lands at the
        // same column as in block 2.
        var firstGrid = (Grid)stack.Children[0];
        var firstLeft = LeftCellText(firstGrid);
        await Assert.That(firstLeft).Contains("[dim]  9 │ [/]");

        var secondGrid = (Grid)stack.Children[1];
        var secondLeft = LeftCellText(secondGrid);
        await Assert.That(secondLeft).Contains("[dim]100 │ [/]");

        await Assert.That(secondLeft.IndexOf('│')).IsEqualTo(firstLeft.IndexOf('│'));
    }
}