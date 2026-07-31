using PhiCoding.Tools.Details;
using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tests;

public class SideBySideDiffTests
{
    private static EditDetails Edit(string oldText, string newText) =>
        new("/tmp/a.cs",
            [new EditOpDetails(oldText, newText)],
            Diff: "",
            Patch: "",
            FirstChangedLine: null);

    private static Grid Build(string oldText, string newText) =>
        (Grid)SideBySideDiff.Build(Edit(oldText, newText))!;

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

        // Both columns carry their own line numbers.
        await Assert.That(left).Contains("[dim]1 │ [/]line 1");
        await Assert.That(right).Contains("[dim]1 │ [/]line 1");
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
            var lSep = leftRows[i].IndexOf("│", StringComparison.Ordinal);
            var rSep = rightRows[i].IndexOf("│", StringComparison.Ordinal);
            await Assert.That(rSep).IsEqualTo(lSep);
        }
    }

    [Test]
    public async Task Build_MultipleEdits_StacksOneGridPerEdit()
    {
        var details = new EditDetails(
            "/tmp/a.cs",
            [
                new EditOpDetails("oldA", "newA"),
                new EditOpDetails("oldB", "newB"),
            ],
            Diff: "",
            Patch: "",
            FirstChangedLine: null);

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
            [new EditOpDetails("oldA", "newA")],
            Diff: "",
            Patch: "",
            FirstChangedLine: null);

        var visual = SideBySideDiff.Build(details);

        await Assert.That(visual).IsTypeOf<Grid>();
    }
}