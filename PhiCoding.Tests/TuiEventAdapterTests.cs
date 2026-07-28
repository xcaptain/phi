using System.Text.Json.Nodes;
using PhiAgent;
using PhiCoding.Tools.Details;
using PhiCoding.Tui;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace PhiCoding.Tests;

public class TuiEventAdapterTests
{
    [Test]
    public async Task TurnStart_SetsRunningAndTurnNumber()
    {
        var state = new TuiState();
        TuiEventAdapter.Apply(state, new TurnStartEvent(3));
        await Assert.That(state.IsRunning).IsTrue();
        await Assert.That(state.CurrentTurn).IsEqualTo(3);
    }

    [Test]
    public async Task TextDelta_CreatesAssistantItemAndAccumulates()
    {
        var state = new TuiState();
        TuiEventAdapter.Apply(state, new AssistantTextDeltaEvent("hel"));
        TuiEventAdapter.Apply(state, new AssistantTextDeltaEvent("lo"));

        await Assert.That(state.Items.Count).IsEqualTo(1);
        var item = state.Items[0];
        await Assert.That(item.Kind).IsEqualTo(ChatItemKind.Assistant);
        await Assert.That(item.Text.ToString()).IsEqualTo("hello");
    }

    [Test]
    public async Task TextDelta_AfterToolCall_StartsNewAssistantItem()
    {
        var state = new TuiState();
        TuiEventAdapter.Apply(state, new AssistantTextDeltaEvent("first"));
        TuiEventAdapter.Apply(state, new AssistantToolCallEvent(new ToolCall("c1", "read")));
        TuiEventAdapter.Apply(state, new AssistantTextDeltaEvent("second"));

        await Assert.That(state.Items.Count).IsEqualTo(3);
        await Assert.That(state.Items[2].Text.ToString()).IsEqualTo("second");
    }

    [Test]
    public async Task ToolCall_BashWithCommand_ShowsDollarPrefix()
    {
        var state = new TuiState();
        var call = new ToolCall("call_abc", "bash")
        {
            Arguments = (JsonObject)JsonNode.Parse("""{"command":"ls -la"}""")!,
        };
        TuiEventAdapter.Apply(state, new AssistantToolCallEvent(call));

        var item = state.Items[^1];
        await Assert.That(item.Kind).IsEqualTo(ChatItemKind.Tool);
        await Assert.That(item.StyledLines![0].Text).IsEqualTo("$ ls -la");
        await Assert.That(item.StyledLines[0].Style).IsEqualTo(TranscriptStyle.ToolCall);
    }

    [Test]
    public async Task ToolCall_EditWithPath_ShowsArrowPrefix()
    {
        var state = new TuiState();
        var call = new ToolCall("call_abc", "edit")
        {
            Arguments = (JsonObject)JsonNode.Parse("""{"path":"/tmp/foo.cs"}""")!,
        };
        TuiEventAdapter.Apply(state, new AssistantToolCallEvent(call));
        await Assert.That(state.Items[^1].StyledLines![0].Text).IsEqualTo("→ edit /tmp/foo.cs");
    }

    [Test]
    public async Task ToolEnd_AppendsResultToMatchingInvocation()
    {
        var state = new TuiState();
        var call = new ToolCall("c1", "bash");
        TuiEventAdapter.Apply(state, new AssistantToolCallEvent(call));
        TuiEventAdapter.Apply(state, new ToolExecutionEndEvent(
            call, new ToolResult([new TextBlock("ok")])));

        await Assert.That(state.Items.Count).IsEqualTo(1);
        var lines = state.Items[0].StyledLines!;
        await Assert.That(lines.Count).IsGreaterThan(1);
        await Assert.That(lines[1].Text).Contains("✓");
        await Assert.That(lines[1].Style).IsEqualTo(TranscriptStyle.ToolOk);
        await Assert.That(lines.Any(l => l.Text.Contains("ok"))).IsTrue();
    }

    [Test]
    public async Task ToolEnd_ErrorResult_MarkedAsError()
    {
        var state = new TuiState();
        var call = new ToolCall("c1", "bash");
        TuiEventAdapter.Apply(state, new AssistantToolCallEvent(call));
        TuiEventAdapter.Apply(state, new ToolExecutionEndEvent(
            call, new ToolResult([new TextBlock("boom")], IsError: true)));

        var item = state.Items[0];
        await Assert.That(item.IsError).IsTrue();
        await Assert.That(item.StyledLines![1].Text).Contains("✗");
        await Assert.That(item.StyledLines[1].Style).IsEqualTo(TranscriptStyle.ToolError);
    }

    [Test]
    public async Task ToolEnd_TruncatesLongOutput()
    {
        var state = new TuiState();
        var call = new ToolCall("c1", "bash");
        var longOutput = string.Join('\n', Enumerable.Range(0, 50).Select(i => $"line {i}"));
        TuiEventAdapter.Apply(state, new AssistantToolCallEvent(call));
        TuiEventAdapter.Apply(state, new ToolExecutionEndEvent(
            call, new ToolResult([new TextBlock(longOutput)])));

        var lines = state.Items[0].StyledLines!;
        await Assert.That(lines.Count).IsLessThan(50);
        await Assert.That(lines[^1].Text).Contains("hidden");
    }

    [Test]
    public async Task ToolEnd_EditResult_RendersColoredDiff()
    {
        var state = new TuiState();
        var call = new ToolCall("c1", "edit");
        var patch = "--- a/f.cs\n+++ b/f.cs\n@@ -1 +1 @@\n-old\n+new\n";
        var result = new ToolResult(
            [new TextBlock("edited")],
            Details: ToolDetails.Node(new EditDetails("f.cs", "old", "new", patch)));

        TuiEventAdapter.Apply(state, new AssistantToolCallEvent(call));
        TuiEventAdapter.Apply(state, new ToolExecutionEndEvent(call, result));

        var lines = state.Items[0].StyledLines!;
        await Assert.That(lines.Any(l => l.Style == TranscriptStyle.DiffAdded && l.Text.Contains("+new"))).IsTrue();
        await Assert.That(lines.Any(l => l.Style == TranscriptStyle.DiffRemoved && l.Text.Contains("-old"))).IsTrue();
    }

    [Test]
    public async Task TurnEnd_ClearsRunningAndStoresUsage()
    {
        var state = new TuiState();
        TuiEventAdapter.Apply(state, new TurnStartEvent(1));
        TuiEventAdapter.Apply(state, new TurnEndEvent(new AssistantMessage
        {
            StopReason = StopReasons.Stop,
            Usage = new Usage { Input = 100, Output = 42, TotalTokens = 142 },
        }));

        await Assert.That(state.IsRunning).IsFalse();
        await Assert.That(state.LastUsage.Output).IsEqualTo(42);
    }

    [Test]
    public async Task HarnessError_AddsErrorItemAndClearsRunning()
    {
        var state = new TuiState();
        TuiEventAdapter.Apply(state, new TurnStartEvent(1));
        TuiEventAdapter.Apply(state, new HarnessErrorEvent("provider down"));

        await Assert.That(state.IsRunning).IsFalse();
        var item = state.Items[^1];
        await Assert.That(item.Kind).IsEqualTo(ChatItemKind.Error);
        await Assert.That(item.Text.ToString()).Contains("provider down");
    }

    [Test]
    public async Task AddUserMessage_AppendsUserItem()
    {
        var state = new TuiState();
        state.AddUserMessage("hi there");
        var item = state.Items[^1];
        await Assert.That(item.Kind).IsEqualTo(ChatItemKind.User);
        await Assert.That(item.Text.ToString()).IsEqualTo("hi there");
    }

    [Test]
    public async Task EveryMutation_RaisesChanged()
    {
        var state = new TuiState();
        var count = 0;
        state.Changed += () => count++;

        TuiEventAdapter.Apply(state, new TurnStartEvent(1));
        TuiEventAdapter.Apply(state, new AssistantTextDeltaEvent("x"));
        TuiEventAdapter.Apply(state, new TurnEndEvent(new AssistantMessage()));

        await Assert.That(count).IsEqualTo(3);
    }
}
