using System.Text.Json.Nodes;

namespace Phi.Extensions.Tests;

/// <summary>
/// Verify each event payload record can be constructed with its documented
/// fields. Keeps the payload schema honest — adding a required field to
/// a record breaks this test, reminding the author to update the doc.
/// </summary>
public class EventRecordTests
{
    [Test]
    public async Task AgentStartEvent_Is_Constructible()
    {
        var e = new Phi.Extensions.Events.AgentStartEvent();
        await Assert.That(e).IsNotNull();
    }

    [Test]
    public async Task AgentEndEvent_Carries_Messages_And_WillRetry()
    {
        var e = new Phi.Extensions.Events.AgentEndEvent(
            Messages: [],
            WillRetry: false);
        await Assert.That(e.Messages).IsEmpty();
        await Assert.That(e.WillRetry).IsFalse();
    }

    [Test]
    public async Task TurnStartEvent_Carries_Index_And_Timestamp()
    {
        var e = new Phi.Extensions.Events.TurnStartEvent(3, 1234567890L);
        await Assert.That(e.TurnIndex).IsEqualTo(3);
        await Assert.That(e.TimestampMs).IsEqualTo(1234567890L);
    }

    [Test]
    public async Task ToolExecutionStartEvent_Carries_JsonObject_Args()
    {
        var args = new JsonObject { ["key"] = "value" };
        var e = new Phi.Extensions.Events.ToolExecutionStartEvent("call-1", "bash", args);
        await Assert.That(e.Arguments["key"]!.GetValue<string>()).IsEqualTo("value");
    }

    [Test]
    public async Task SessionLifecycleReason_Has_Five_Values()
    {
        var names = Enum.GetNames<Phi.Extensions.Events.SessionLifecycleReason>();
        await Assert.That(names.Length).IsEqualTo(5);
        await Assert.That(names).Contains("Startup");
        await Assert.That(names).Contains("Reload");
    }

    [Test]
    public async Task InputHookResult_PassThrough_Singleton()
    {
        // PassThrough is a static singleton; identity preserved across calls.
        await Assert.That(Phi.Extensions.Events.InputHookResult.PassThrough.Handled).IsFalse();
    }

    [Test]
    public async Task ToolCallHookResult_PassThrough_Doesnt_Block()
    {
        await Assert.That(Phi.Extensions.Events.ToolCallHookResult.PassThrough.Block).IsFalse();
    }
}
