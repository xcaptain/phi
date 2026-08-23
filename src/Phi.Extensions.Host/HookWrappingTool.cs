using System.Text.Json.Nodes;
using Phi.Agent;

namespace Phi.Extensions.Host;

/// <summary>
/// Decorator that fires <c>tool_call</c> / <c>tool_result</c> hooks around an
/// inner <see cref="Tool"/>. Session.RegisterExtensionTool receives the
/// wrapped tool; the harness invokes this wrapper, which consults the
/// shared <see cref="HookRegistry"/> before/after delegating to the real
/// tool. Because the registry is shared (not captured at wrap time), hooks
/// registered later automatically apply to already-wrapped tools.
/// </summary>
internal sealed class HookWrappingTool : Tool
{
    private readonly Tool _inner;
    private readonly HookRegistry _hooks;

    public HookWrappingTool(Tool inner, HookRegistry hooks)
    {
        _inner = inner;
        _hooks = hooks;
    }

    public override string Name => _inner.Name;
    public override string Description => _inner.Description;
    public override JsonObject Parameters => _inner.Parameters;

    public override async Task<ToolResult> ExecuteAsync(
        string toolName,
        string toolCallId,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        // Fire tool_call hooks; block if any handler blocked.
        var callResult = _hooks.RunToolCallHooks(_inner.Name, arguments);
        if (callResult.Block)
        {
            return new ToolResult(
                [new TextBlock(callResult.Reason ?? $"tool call to '{_inner.Name}' blocked")],
                IsError: true);
        }

        var finalArgs = callResult.Arguments ?? arguments;

        var result = await _inner.ExecuteAsync(toolName, toolCallId, finalArgs, cancellationToken);

        // Fire tool_result hooks; chain rewrites content / details.
        return _hooks.RunToolResultHooks(_inner.Name, finalArgs, result);
    }
}
