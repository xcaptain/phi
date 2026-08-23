using System.Text.Json.Nodes;
using Phi.Agent;
using Phi.Extensions.Events;
using Phi.Provider;

namespace Phi.Extensions.Host.Tests;

/// <summary>
/// Tests the <see cref="HookRegistry"/> + <see cref="HookWrappingTool"/>
/// interception pipeline: tool_call block, argument transformation,
/// tool_result rewrite, input transform, and fail-safe on handler throw.
/// </summary>
public class HookDispatchTests
{
    private readonly HookRegistry _registry = new();

    private static Tool EchoTool(string name = "echo") => new AnonymousTool(name);

    private sealed class AnonymousTool(string name) : Tool
    {
        public override string Name => name;
        public override string Description => "echo tool";
        public override JsonObject Parameters => new() { ["type"] = "object" };

        public override Task<ToolResult> ExecuteAsync(
            string toolName, string toolCallId, JsonObject arguments, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult([new TextBlock("ran")]));
    }

    /// <summary>A loaded-extension identity without spinning a real ALC.</summary>
    private static LoadedExtension FakeExt(string name = "test-ext") =>
        new(
            name, "0.0.0", "",
            typeof(HookDispatchTests), new FakePhiExtension(),
            "", typeof(HookDispatchTests).Assembly, new ExtensionLoadContext());

    private sealed class FakePhiExtension : IPhiExtension
    {
        public void Setup(IPhiApi api) { }
    }

    [Test]
    public async Task ToolCall_Block_Returns_Error_Result()
    {
        // Register a block handler via the registry directly (no extension
        // needed for the hook mechanics).
        var identity = FakeExt("test-ext");
        _registry.RegisterToolCall(identity, (PhiEvent ev, IPhiContext _) =>
        {
            if (ev is ToolCallHookEvent tce)
                tce.Result = new ToolCallHookResult { Block = true, Reason = "blocked by test" };
            return ValueTask.CompletedTask;
        });

        var tool = new HookWrappingTool(EchoTool(), _registry);
        var result = await tool.ExecuteAsync("echo", "call-1",
            new JsonObject { ["command"] = "rm -rf /" }, default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("blocked by test");
    }

    [Test]
    public async Task ToolCall_No_Hook_Passes_Through()
    {
        var tool = new HookWrappingTool(EchoTool(), _registry);
        var result = await tool.ExecuteAsync("echo", "call-1", new JsonObject(), default);

        await Assert.That(result.IsError).IsFalse();
        await Assert.That(result.Text).IsEqualTo("ran");
    }

    [Test]
    public async Task ToolResult_Rewrites_Content()
    {
        var identity = FakeExt("test-ext");
        _registry.RegisterToolResult(identity, (PhiEvent ev, IPhiContext _) =>
        {
            if (ev is ToolResultHookEvent tre)
                tre.Rewrite = new ToolResultHookResult { Content = [new TextBlock("rewritten")] };
            return ValueTask.CompletedTask;
        });

        var tool = new HookWrappingTool(EchoTool(), _registry);
        var result = await tool.ExecuteAsync("echo", "call-1", new JsonObject(), default);

        await Assert.That(result.Text).IsEqualTo("rewritten");
    }

    [Test]
    public async Task ToolCall_Handler_Exception_Is_FailSafe_Block()
    {
        var identity = FakeExt("test-ext");
        _registry.RegisterToolCall(identity, (PhiEvent ev, IPhiContext _) =>
        {
            throw new InvalidOperationException("handler blew up");
        });

        var tool = new HookWrappingTool(EchoTool(), _registry);
        var result = await tool.ExecuteAsync("echo", "call-1", new JsonObject(), default);

        await Assert.That(result.IsError).IsTrue();
        await Assert.That(result.Text).Contains("hook error");
    }

    [Test]
    public async Task Input_Hook_Can_Transform_Text()
    {
        var identity = FakeExt("test-ext");
        _registry.RegisterInput(identity, (PhiEvent ev, IPhiContext _) =>
        {
            if (ev is InputEvent ie)
                ie.Result = new InputHookResult { Text = ie.Text + " (augmented)" };
            return ValueTask.CompletedTask;
        });

        var result = _registry.RunInputHooks("do the thing", InputSource.EditorAccepted);

        await Assert.That(result.Handled).IsFalse();
        await Assert.That(result.Text).IsEqualTo("do the thing (augmented)");
    }

    [Test]
    public async Task Input_Hook_Can_Consume()
    {
        var identity = FakeExt("test-ext");
        _registry.RegisterInput(identity, (PhiEvent ev, IPhiContext _) =>
        {
            if (ev is InputEvent ie)
                ie.Result = new InputHookResult { Handled = true, Message = "consumed" };
            return ValueTask.CompletedTask;
        });

        var result = _registry.RunInputHooks("do the thing", InputSource.EditorAccepted);

        await Assert.That(result.Handled).IsTrue();
    }

}
