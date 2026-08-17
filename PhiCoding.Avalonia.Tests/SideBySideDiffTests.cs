using Avalonia.Controls;
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
/// </summary>
[NotInParallel("Avalonia-UI")]
public class SideBySideDiffTests
{
    private static EditDetails Edit(string oldText, string newText) =>
        new("/tmp/a.cs",
            [new EditOpDetails(oldText, newText)],
            Diff: "",
            Patch: "",
            FirstChangedLine: null);

    private static Grid BuildSingle(string oldText, string newText) =>
        (Grid)SideBySideDiff.Build(Edit(oldText, newText));

    /// <summary>Walks a column StackPanel to its first row DockPanel.</summary>
    private static DockPanel RowAt(StackPanel column, int index) =>
        (DockPanel)column.Children[index];

    /// <summary>Returns the TextBlock children of one row (number, then text).</summary>
    private static (TextBlock number, TextBlock text) RowTexts(DockPanel row)
    {
        // DockPanel: first child (Dock.Left) is the number, second (last,
        // fill) is the text body. Both children are TextBlocks.
        var number = (TextBlock)row.Children[0];
        var text = (TextBlock)row.Children[1];
        return (number, text);
    }

    /// <summary>Pulls the left / right StackPanel from a two-column diff Grid.</summary>
    private static (StackPanel left, StackPanel right) Columns(Grid grid)
    {
        var left = (StackPanel)grid.Children[0];
        var right = (StackPanel)grid.Children[1];
        return (left, right);
    }

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
        var (_, leftText) = RowTexts(RowAt(left, 1));
        var (_, rightText) = RowTexts(RowAt(right, 1));
        await Assert.That(leftText.Text).IsEqualTo("- old line 2");
        await Assert.That(leftText.Foreground).IsEqualTo(AvaloniaTheme.Danger);
        await Assert.That(rightText.Text).IsEqualTo("+ new line 2");
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

        var (_, rightText) = RowTexts(RowAt(right, right.Children.Count - 1));
        await Assert.That(rightText.Text).IsEqualTo("+ c");
        await Assert.That(rightText.Foreground).IsEqualTo(AvaloniaTheme.Success);

        // The matching row on the left has no text (Imaginary).
        var (_, leftText) = RowTexts(RowAt(left, left.Children.Count - 1));
        await Assert.That(leftText.Text).IsEqualTo("  ");
    }

    [Test]
    public async Task Build_DeletedLine_LeftColumnShowsItInDanger()
    {
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle("a\nb\nc", "a\nc");
        var (left, right) = Columns(grid);

        var (_, leftText) = RowTexts(RowAt(left, 1));
        await Assert.That(leftText.Text).IsEqualTo("- b");
        await Assert.That(leftText.Foreground).IsEqualTo(AvaloniaTheme.Danger);
    }

    [Test]
    public async Task Build_LineNumbers_AreOneBasedPerSide()
    {
        AvaloniaTestHost.EnsureInitialized();
        var grid = BuildSingle("l1\nl2\nl3", "l1\nl2\nl3");
        var (left, _) = Columns(grid);

        var (n0, _) = RowTexts(RowAt(left, 0));
        var (n1, _) = RowTexts(RowAt(left, 1));
        var (n2, _) = RowTexts(RowAt(left, 2));
        await Assert.That(n0.Text).IsEqualTo("1 │ ");
        await Assert.That(n1.Text).IsEqualTo("2 │ ");
        await Assert.That(n2.Text).IsEqualTo("3 │ ");
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

        var (_, text) = RowTexts(RowAt(left, 0));
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
            var (ln, _) = RowTexts(RowAt(left, i));
            var (rn, _) = RowTexts(RowAt(right, i));
            await Assert.That(rn.Text.Length).IsEqualTo(ln.Text.Length);
        }
    }

    [Test]
    public async Task Build_MultipleEdits_StacksOneGridPerEdit()
    {
        AvaloniaTestHost.EnsureInitialized();
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
            [new EditOpDetails("oldA", "newA")],
            Diff: "",
            Patch: "",
            FirstChangedLine: null);

        var visual = SideBySideDiff.Build(details);
        await Assert.That(visual).IsTypeOf<Grid>();
    }
}
