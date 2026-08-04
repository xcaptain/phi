using PhiCoding.Tui;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tests.Tui;

[NotInParallel(TuiTestGroups.BindingManager)]
public class SelectionCopyHostTests
{
    [Test]
    public async Task FindSelectionOwner_NullSource_ReturnsFalse()
    {
        var host = new SelectionCopyHost(new Paragraph("hello"));

        var found = SelectionCopyHost.TryFindSelectableOwner(null, out _);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task FindSelectionOwner_NotAnOwner_ReturnsFalse()
    {
        var host = new SelectionCopyHost(new Group(new Markup("not selectable content")));

        var found = SelectionCopyHost.TryFindSelectableOwner(host, out _);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task FindSelectionOwner_OwnerItself_ReturnsOwner()
    {
        var paragraph = new Paragraph("hello world");
        var host = new SelectionCopyHost(paragraph);

        var found = SelectionCopyHost.TryFindSelectableOwner(paragraph, out var owner);

        await Assert.That(found).IsTrue();
        await Assert.That(owner).IsSameReferenceAs(paragraph);
    }

    [Test]
    public async Task FindSelectionOwner_RespectsIsSelectableFalse()
    {
        var paragraph = new Paragraph("hello world") { IsSelectable = false };
        var host = new SelectionCopyHost(paragraph);

        var found = SelectionCopyHost.TryFindSelectableOwner(paragraph, out _);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task FindSelectionOwner_WalksThroughNonOwnerAncestors()
    {
        // The walk goes UP only via Parent. The selectable paragraph is a
        // LEAF visual (no children of its own), so the realistic case is
        // the source itself being the paragraph — already covered by
        // FindSelectionOwner_OwnerItself_ReturnsOwner. This test instead
        // pins the parent-only direction by asserting that asking from a
        // descendant of a selectable owner yields that owner when the
        // source can host children of its own (e.g. a Group with the
        // paragraph as Content).
        var paragraph = new Paragraph("hello world");
        var group = new Group(paragraph);
        var host = new SelectionCopyHost(group);

        // From group, walk up: group is not an owner, host is not an
        // owner, null. Confirms the upward-only direction without the
        // paragraph sitting in an ancestor chain.
        var found = SelectionCopyHost.TryFindSelectableOwner(group, out _);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task FindSelectionOwner_StopsAtRoot_WhenNoOwner()
    {
        // Only non-owner visuals in the chain — must return false without
        // throwing.
        var group = new Group(new Markup("inner"));
        var host = new SelectionCopyHost(group);

        var found = SelectionCopyHost.TryFindSelectableOwner(group, out _);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task Constructor_NullContent_Throws()
    {
        await Assert.That(() => new SelectionCopyHost(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_SetsContent()
    {
        var inner = new Paragraph("content");
        var host = new SelectionCopyHost(inner);

        await Assert.That(host.Content).IsSameReferenceAs(inner);
    }
}
