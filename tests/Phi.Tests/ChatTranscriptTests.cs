using System.Reflection;
using Phi.Agent;
using Phi.Tests.Helpers;
using Phi.Tui.Components;
using Phi.Tui.Components.ToolCards;

namespace Phi.Tests;

[NotInParallel(TuiTestGroups.BindingManager)]
public class ChatTranscriptTests
{
    [Test]
    public async Task FormatThinkingText_SingleLine_WrapsWithDim()
    {
        var result = ChatTranscript.FormatThinkingText("Step 1: read the file");

        await Assert.That(result).IsEqualTo("[dim]Step 1: read the file[/]");
    }

    [Test]
    public async Task FormatThinkingText_MultiLine_WrapsEachLine()
    {
        var result = ChatTranscript.FormatThinkingText("Step 1: read\nStep 2: edit");

        await Assert.That(result).IsEqualTo(
            "[dim]Step 1: read[/]\n[dim]Step 2: edit[/]");
    }

    [Test]
    public async Task FormatThinkingText_BracketCharacters_AreEscaped()
    {
        // The model may emit [dim] or [bold] literally in its thinking;
        // they must be escaped so the markup parser doesn't interpret them.
        var result = ChatTranscript.FormatThinkingText("Use [bold] markup carefully");

        await Assert.That(result).IsEqualTo(
            "[dim]Use \\[bold\\] markup carefully[/]");
    }

    [Test]
    public async Task FormatThinkingText_CrlfLineEndings_AreNormalized()
    {
        var result = ChatTranscript.FormatThinkingText("Line A\r\nLine B");

        await Assert.That(result).IsEqualTo(
            "[dim]Line A[/]\n[dim]Line B[/]");
    }

    [Test]
    public async Task FormatThinkingText_EmptyString_YieldsEmptyWrapper()
    {
        var result = ChatTranscript.FormatThinkingText("");

        await Assert.That(result).IsEqualTo("[dim][/]");
    }

    [Test]
    public async Task FormatThinkingDuration_SubSecond_AsMilliseconds()
    {
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMilliseconds(0)))
            .IsEqualTo("0ms");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMilliseconds(500)))
            .IsEqualTo("500ms");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMilliseconds(999)))
            .IsEqualTo("999ms");
    }

    [Test]
    public async Task FormatThinkingDuration_SubMinute_AsSecondsWithDecimal()
    {
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(1)))
            .IsEqualTo("1.0s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMilliseconds(1500)))
            .IsEqualTo("1.5s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(45)))
            .IsEqualTo("45.0s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(59.9)))
            .IsEqualTo("59.9s");
    }

    [Test]
    public async Task FormatThinkingDuration_OverMinute_AsMinutesAndSeconds()
    {
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(60)))
            .IsEqualTo("1m0s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromSeconds(125)))
            .IsEqualTo("2m5s");
        await Assert.That(ChatTranscript.FormatThinkingDuration(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(42)))
            .IsEqualTo("3m42s");
    }

    [Test]
    public async Task ReadToolCall_RendersSingleLine_NoPendingGroup()
    {
        // read results render as ONE Markup line (invocation + summary),
        // never the pending "Group(title, body)" card that other tools use.
        var session = new MockSession();
        var transcript = new ChatTranscript();
        transcript.Bind(session);
        var args = new System.Text.Json.Nodes.JsonObject
        {
            ["path"] = "a.cs",
            ["offset"] = 30,
            ["limit"] = 18,
        };
        var call = new ToolCall("c1", "read") { Arguments = args };

        session.EmitHarnessEvent(new AssistantToolCallEvent(call));

        // Flow must contain exactly one item.
        var flow = transcript.Flow;
        await Assert.That(flow.Items.Count).IsEqualTo(1);

        var toolCards = (System.Collections.IDictionary)
            typeof(ChatTranscript)
                .GetField("_toolCards", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(transcript)!;
        var card = (ReadToolCard)toolCards["c1"]!;
        // The read card's Visual is a single Markup, not a Group with body.
        await Assert.That(card.Visual).IsTypeOf<XenoAtom.Terminal.UI.Controls.Markup>();
        await Assert.That(card.Title).Contains("[offset=30, limit=18]");
    }

    [Test]
    public async Task CompleteReadToolCall_UpdatesSingleLineTitle_WithUnescapedRange()
    {
        var session = new MockSession();
        var transcript = new ChatTranscript();
        transcript.Bind(session);
        var args = new System.Text.Json.Nodes.JsonObject
        {
            ["path"] = "a.cs",
            ["offset"] = 30,
            ["limit"] = 18,
        };
        var call = new ToolCall("c1", "read") { Arguments = args };

        session.EmitHarnessEvent(new AssistantToolCallEvent(call));
        session.EmitHarnessEvent(new ToolExecutionEndEvent(
            call,
            new ToolResult(
                [new TextBlock("file body")],
                Details: Phi.Extensions.CodingPack.Tools.Details.ToolDetails.Node(
                    new Phi.Extensions.CodingPack.Tools.Details.ReadDetails(
                        "a.cs", Offset: 30, Limit: 18,
                        LineCount: 18, TotalLineCount: 82, ByteCount: 2048)))));

        var toolCards = (System.Collections.IDictionary)
            typeof(ChatTranscript)
                .GetField("_toolCards", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(transcript)!;
        var card = (ReadToolCard)toolCards["c1"]!;

        // Final line carries the status + the range hint. The hint is
        // author-controlled text rendered literally by XenoAtom (unknown
        // markup tags like "[offset=30, limit=18]" pass through unchanged),
        // so it must appear unescaped in the title.
        await Assert.That(card.Title).Contains("[offset=30, limit=18]");
        await Assert.That(card.Title).DoesNotContain("\\[");
        await Assert.That(card.Title).Contains("read — lines 30-47 of 82");
    }

    [Test]
    public async Task EditToolCall_Complete_SwapsBodyStateToDiffGrid()
    {
        // Regression: the edit body must render as a side-by-side diff. The
        // body is a State<Visual> fed into a ComputedVisual; on completion
        // CompleteTool swaps BodyState.Value to the diff Grid, which the
        // already-laid-out Group re-renders in place.
        var session = new MockSession();
        var transcript = new ChatTranscript();
        transcript.Bind(session);
        var call = new ToolCall("e1", "edit")
        {
            Arguments = new System.Text.Json.Nodes.JsonObject { ["path"] = "a.cs" },
        };

        session.EmitHarnessEvent(new AssistantToolCallEvent(call));

        var toolCards = (System.Collections.IDictionary)
            typeof(ChatTranscript)
                .GetField("_toolCards", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(transcript)!;
        var card = (EditToolCard)toolCards["e1"]!;
        // Pending body is the placeholder Markup.
        await Assert.That(card.BodyState.Value).IsTypeOf<XenoAtom.Terminal.UI.Controls.Markup>();

        // Complete the edit with an EditDetails result.
        session.EmitHarnessEvent(new ToolExecutionEndEvent(
            call,
            new ToolResult(
                [new TextBlock("ok")],
                Details: Phi.Extensions.CodingPack.Tools.Details.ToolDetails.Node(
                    new Phi.Extensions.CodingPack.Tools.Details.EditDetails(
                        "a.cs",
                        [new Phi.Extensions.CodingPack.Tools.Details.EditOpDetails("old line", "new line")],
                        Diff: "",
                        Patch: "")))));

        // Body state now holds the diff Grid.
        await Assert.That(card.BodyState.Value).IsTypeOf<XenoAtom.Terminal.UI.Controls.Grid>();
        var grid = (XenoAtom.Terminal.UI.Controls.Grid)card.BodyState.Value!;
        await Assert.That(grid.Cells.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SubmitCustomLine_AddsCustomLineVisual_WithoutRenderer_FallsBackToText()
    {
        var session = new MockSession();
        var transcript = new ChatTranscript();
        transcript.Bind(session);
        var before = transcript.Flow.Items.Count;

        // No renderer registered for "my-ext:progress" → the fallback plain
        // text bubble is used. The line must still land in the flow.
        transcript.SubmitCustomLine(new Phi.Extensions.TranscriptLine(
            Type: "my-ext:progress",
            Id: "line-1",
            Content: "Building…",
            Details: new Dictionary<string, object?> { ["percent"] = 42 }));

        await Assert.That(transcript.Flow.Items.Count).IsEqualTo(before + 1);
    }

    [Test]
    public async Task SubmitCustomLine_WithRegisteredRenderer_UsesRendererVisual()
    {
        var session = new MockSession();
        // A fake renderers source that registers a custom renderer for
        // "my-ext:styled" returning a simple Visual.
        var renderers = new FakeExtensionRenderers();
        var transcript = new ChatTranscript();
        transcript.Bind(session, renderers);

        transcript.SubmitCustomLine(new Phi.Extensions.TranscriptLine(
            Type: "my-ext:styled",
            Id: "line-2",
            Content: "fancy body"));

        await Assert.That(transcript.Flow.Items.Count).IsEqualTo(1);
    }

    /// <summary>Stub renderer registry for the TUI-side routing test.</summary>
    private sealed class FakeExtensionRenderers : Phi.Chat.IExtensionRenderers
    {
        public bool TryGetToolDescriptor(string toolName, out Phi.Agent.ToolDescriptor descriptor)
        {
            descriptor = Phi.Agent.ToolDescriptors.For(toolName);
            return false;
        }

        public bool TryGetToolCardRenderer(string toolName, out object renderer)
        {
            renderer = null!;
            return false;
        }

        public bool TryGetTranscriptLineRenderer(string lineType, out object renderer)
        {
            if (lineType == "my-ext:styled")
            {
                renderer = new Phi.Extensions.Rendering.TranscriptLineRenderer((line, expanded) =>
                    new XenoAtom.Terminal.UI.Controls.Markup("[green]" + line.Content + "[/]"));
                return true;
            }
            renderer = null!;
            return false;
        }

        public bool TryGetMessageRenderer(string customType, out object renderer)
        {
            renderer = null!;
            return false;
        }
    }
}
