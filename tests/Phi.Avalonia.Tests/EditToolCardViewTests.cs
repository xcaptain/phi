using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Phi.Agent;
using Phi.Avalonia.Components;
using Phi.Avalonia.Components.ToolCards;
using Phi.Extensions.CodingPack.Tools.Details;

namespace Phi.Avalonia.Tests;

/// <summary>
/// <see cref="EditToolCardView"/>: header is <c>✓ edit: &lt;path&gt;</c>
/// (or <c>✗ edit: &lt;path&gt;</c> on failure). On success the body is the
/// <see cref="SideBySideDiff"/> grid wrapped in a
/// <see cref="ToolCardBodyFrame"/> with horizontal scrolling disabled so the
/// two diff columns get a bounded width — long lines wrap instead of
/// overflowing into a horizontal scrollbar, and the Grid's star columns stay
/// equal so multi-block diffs align.
/// </summary>
[NotInParallel("Avalonia-UI")]
public class EditToolCardViewTests
{
    private static ToolCall Call(string path)
        => new("id-1", "edit") { Arguments = new JsonObject { ["path"] = path } };

    private static ToolResult EditResult(EditDetails details, bool isError = false) =>
        new(
            [new Phi.Agent.TextBlock("")],
            Details: ToolDetails.Node(details),
            IsError: isError);

    private static ToolCardBodyFrame FrameOf(EditToolCardView card)
    {
        var section = (CollapsibleSection)card.Visual;
        section.IsExpanded = true;  // force the lazy body to build
        return (ToolCardBodyFrame)section.BodyContent;
    }

    [Test]
    public async Task Complete_Success_DiffBodyDisablesHorizontalScroll()
    {
        // The diff must not produce a horizontal scrollbar: text wraps
        // inside the bounded columns instead, so the user sees both the
        // old and new content without dragging.
        AvaloniaTestHost.EnsureInitialized();
        var card = new EditToolCardView();
        card.ShowPending(Call("/tmp/a.cs"));
        card.Complete(EditResult(new EditDetails(
            "/tmp/a.cs",
            [new EditOpDetails("old line", "new line", 1)],
            Diff: "",
            Patch: "")));

        var frame = FrameOf(card);
        var scroll = (ScrollViewer)frame.Child!;
        await Assert.That(scroll.HorizontalScrollBarVisibility)
            .IsEqualTo(ScrollBarVisibility.Disabled);
    }

    [Test]
    public async Task Complete_Failure_KeepsDefaultHorizontalScroll()
    {
        // Error bodies are plain wrapped text (not a diff grid), so the
        // standard frame behavior (horizontal scroll allowed) is fine.
        AvaloniaTestHost.EnsureInitialized();
        var card = new EditToolCardView();
        card.ShowPending(Call("/tmp/a.cs"));
        card.Complete(EditResult(
            new EditDetails("/tmp/a.cs", [], Diff: "", Patch: ""),
            isError: true));

        var frame = FrameOf(card);
        var scroll = (ScrollViewer)frame.Child!;
        await Assert.That(scroll.HorizontalScrollBarVisibility)
            .IsEqualTo(ScrollBarVisibility.Auto);
    }
}
