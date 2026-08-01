using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace PhiCoding.Tests;

/// <summary>
/// Probes DiffPlex's <see cref="SideBySideDiffBuilder"/> model shape so the
/// renderer can rely on exact enum names / fields. This is a compile-time +
/// behavior guard; if DiffPlex changes the model, this test breaks loudly.
/// </summary>
public class SideBySideDiffModelProbeTests
{
    [Test]
    public async Task BuildDiffModel_OldAndNew_AreEqualLength()
    {
        var model = new SideBySideDiffBuilder(DiffPlex.Differ.Instance)
            .BuildDiffModel(
                "line 1\nold line 2\nline 3",
                "line 1\nnew line 2\nline 3");

        await Assert.That(model.OldText.Lines.Count).IsEqualTo(model.NewText.Lines.Count);
        // 3 context-anchored rows: (1,1), (2 old / 2 new), (3,3)
        await Assert.That(model.OldText.Lines.Count).IsEqualTo(3);
    }

    [Test]
    public async Task BuildDiffModel_ModifiedPair_IsClassified()
    {
        var model = new SideBySideDiffBuilder(DiffPlex.Differ.Instance)
            .BuildDiffModel("a\nx\nc", "a\ny\nc");

        var oldLine2 = model.OldText.Lines[1];
        var newLine2 = model.NewText.Lines[1];

        // DiffPlex pairs a changed line as Modified on both sides.
        await Assert.That(oldLine2.Type).IsEqualTo(ChangeType.Modified);
        await Assert.That(newLine2.Type).IsEqualTo(ChangeType.Modified);
        await Assert.That(oldLine2.Text).IsEqualTo("x");
        await Assert.That(newLine2.Text).IsEqualTo("y");
        // Positions are 1-based.
        await Assert.That(oldLine2.Position).IsEqualTo(2);
        await Assert.That(newLine2.Position).IsEqualTo(2);
    }

    [Test]
    public async Task BuildDiffModel_InsertAndDelete_PadWithImaginary()
    {
        var model = new SideBySideDiffBuilder(DiffPlex.Differ.Instance)
            .BuildDiffModel("a\nb\nc", "a\nb\nc\nd");

        // 4 rows on both sides; the extra new row pads old with Imaginary.
        await Assert.That(model.OldText.Lines.Count).IsEqualTo(4);
        await Assert.That(model.NewText.Lines.Count).IsEqualTo(4);
        var lastOld = model.OldText.Lines[^1];
        var lastNew = model.NewText.Lines[^1];
        await Assert.That(lastOld.Type).IsEqualTo(ChangeType.Imaginary);
        await Assert.That(lastNew.Type).IsEqualTo(ChangeType.Inserted);
        await Assert.That(lastNew.Text).IsEqualTo("d");
    }
}