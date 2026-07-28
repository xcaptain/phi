using DiffPlex.Renderer;
using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class DiffFormatterTests
{
    [Test]
    public async Task Parse_HeaderLines_AreHeaderKind()
    {
        var diff = UnidiffRenderer.GenerateUnidiff("a", "b", "old.txt", "new.txt");
        var lines = DiffFormatter.Parse(diff);

        await Assert.That(lines.Count).IsGreaterThan(0);
        await Assert.That(lines[0].Kind).IsEqualTo(DiffLineKind.Header);
        await Assert.That(lines[0].Text).StartsWith("---");
        await Assert.That(lines[1].Kind).IsEqualTo(DiffLineKind.Header);
        await Assert.That(lines[1].Text).StartsWith("+++");
    }

    [Test]
    public async Task Parse_AddedRemovedContext_ClassifiedCorrectly()
    {
        var diff = UnidiffRenderer.GenerateUnidiff(
            "line 1\nold line 2\nline 3",
            "line 1\nnew line 2\nline 3",
            "a.txt", "b.txt");
        var lines = DiffFormatter.Parse(diff);

        var added = lines.Where(l => l.Kind == DiffLineKind.Added).ToList();
        var removed = lines.Where(l => l.Kind == DiffLineKind.Removed).ToList();
        var context = lines.Where(l => l.Kind == DiffLineKind.Context).ToList();

        await Assert.That(added.Count).IsEqualTo(1);
        await Assert.That(added[0].Text).Contains("new line 2");
        await Assert.That(removed.Count).IsEqualTo(1);
        await Assert.That(removed[0].Text).Contains("old line 2");
        await Assert.That(context.Count).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task Parse_HunkHeader_IsHunkKind()
    {
        var diff = UnidiffRenderer.GenerateUnidiff("x", "y", "a", "b");
        var lines = DiffFormatter.Parse(diff);

        await Assert.That(lines.Any(l => l.Kind == DiffLineKind.Hunk)).IsTrue();
    }

    [Test]
    public async Task Parse_Empty_ReturnsEmpty()
    {
        var lines = DiffFormatter.Parse("");
        await Assert.That(lines).IsEmpty();
    }
}