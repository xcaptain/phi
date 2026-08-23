using Phi.Extensions.Events;

namespace Phi.Extensions.Host;

/// <summary>
/// Holds hook handlers registered via <c>api.On("tool_call" | "tool_result" | "input")</c>
/// and chains them around tool execution / input submission. The hook
/// <em>events</em> (observation) are dispatched by <see cref="EventDispatch"/>;
/// this registry handles the three <em>interception</em> hooks that can
/// change behavior.
/// <list type="bullet">
/// <item><c>tool_call</c> — block or rewrite arguments before a tool runs.</item>
/// <item><c>tool_result</c> — rewrite content / details after a tool runs.</item>
/// <item><c>input</c> — transform or consume a submitted prompt.</item>
/// </list>
/// </summary>
internal sealed class HookRegistry
{
    private readonly List<RegisteredHook> _toolCallHooks = [];
    private readonly List<RegisteredHook> _toolResultHooks = [];
    private readonly List<RegisteredHook> _inputHooks = [];

    /// <summary>A hook handler with the extension identity (for audit + staleness).</summary>
    private sealed record RegisteredHook(
        LoadedExtension Extension,
        object Handler);

    /// <summary>Clears all hook registrations (used on /reload so old handlers die).</summary>
    public void Dispose()
    {
        _toolCallHooks.Clear();
        _toolResultHooks.Clear();
        _inputHooks.Clear();
    }

    // ──────── Registration ────────

    public IDisposable RegisterToolCall(LoadedExtension ext, object handler)
    {
        _toolCallHooks.Add(new RegisteredHook(ext, handler));
        return new RemoveHook(() => _toolCallHooks.RemoveAll(h => h.Extension == ext && h.Handler == handler));
    }

    public IDisposable RegisterToolResult(LoadedExtension ext, object handler)
    {
        _toolResultHooks.Add(new RegisteredHook(ext, handler));
        return new RemoveHook(() => _toolResultHooks.RemoveAll(h => h.Extension == ext && h.Handler == handler));
    }

    public IDisposable RegisterInput(LoadedExtension ext, object handler)
    {
        _inputHooks.Add(new RegisteredHook(ext, handler));
        return new RemoveHook(() => _inputHooks.RemoveAll(h => h.Extension == ext && h.Handler == handler));
    }

    private sealed class RemoveHook(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }

    // ──────── tool_call chaining ────────

    /// <summary>
    /// Runs all tool_call hooks against <paramref name="arguments"/>.
    /// Returns a <see cref="ToolCallHookResult"/>: <c>Block = true</c> if
    /// any handler blocked (first block wins; handlers after the block
    /// are not invoked); otherwise the chained (possibly transformed)
    /// <see cref="ToolCallHookResult.Arguments"/>. A handler exception is
    /// treated as <c>Block = true</c> (fail-safe).
    /// </summary>
    public ToolCallHookResult RunToolCallHooks(
        string toolName, System.Text.Json.Nodes.JsonObject arguments)
    {
        var currentArgs = arguments;
        foreach (var hook in _toolCallHooks)
        {
            var ev = new ToolCallHookEvent(toolName, currentArgs);
            try
            {
                if (hook.Handler is Func<PhiEvent, IPhiContext, ValueTask> asyncHandler)
                {
                    asyncHandler(ev, new NullContext()).AsTask().GetAwaiter().GetResult();
                }
                else if (hook.Handler is Action<PhiEvent, IPhiContext> syncHandler)
                {
                    syncHandler(ev, new NullContext());
                }
            }
            catch (Exception)
            {
                // Fail-safe: an exception in a tool_call hook blocks the call
                // rather than letting a half-transformed tool run.
                return new ToolCallHookResult { Block = true, Reason = $"hook error in {hook.Extension.Name}" };
            }

            var result = ev.Result;
            if (result is null) continue;                       // pass-through

            if (result.Block)
                return result;                                   // first block wins

            if (result.Arguments is not null)
                currentArgs = result.Arguments;                  // chain transform
        }
        return new ToolCallHookResult { Block = false, Arguments = currentArgs };
    }

    // ──────── tool_result chaining ────────

    /// <summary>
    /// Runs all tool_result hooks against the completed <paramref name="result"/>.
    /// Returns the final (possibly rewritten) <see cref="ToolResult"/>.
    /// </summary>
    public Phi.Agent.ToolResult RunToolResultHooks(
        string toolName, System.Text.Json.Nodes.JsonObject arguments, Phi.Agent.ToolResult result)
    {
        var current = result;
        foreach (var hook in _toolResultHooks)
        {
            var ev = new ToolResultHookEvent(toolName, arguments, current);
            try
            {
                if (hook.Handler is Func<PhiEvent, IPhiContext, ValueTask> asyncHandler)
                    asyncHandler(ev, new NullContext()).AsTask().GetAwaiter().GetResult();
                else if (hook.Handler is Action<PhiEvent, IPhiContext> syncHandler)
                    syncHandler(ev, new NullContext());
            }
            catch (Exception)
            {
                // Fail-safe: keep the tool's own result on hook error.
                continue;
            }

            var rewrite = ev.Rewrite;
            if (rewrite is null) continue;
            if (rewrite.Content is not null) current = current with { Content = rewrite.Content };
            if (rewrite.Details is not null) current = current with { Details = rewrite.Details };
        }
        return current;
    }

    // ──────── input chaining ────────

    /// <summary>
    /// Runs all input hooks against <paramref name="text"/>. Returns an
    /// <see cref="InputHookResult"/>: <c>Handled = true</c> if any handler
    /// consumed the prompt; otherwise the chained (possibly transformed)
    /// <see cref="InputHookResult.Text"/>.
    /// </summary>
    public InputHookResult RunInputHooks(string text, InputSource source)
    {
        var current = text;
        foreach (var hook in _inputHooks)
        {
            var ev = new InputEvent(current, source, Streaming: false);
            try
            {
                if (hook.Handler is Func<PhiEvent, IPhiContext, ValueTask> asyncHandler)
                    asyncHandler(ev, new NullContext()).AsTask().GetAwaiter().GetResult();
                else if (hook.Handler is Action<PhiEvent, IPhiContext> syncHandler)
                    syncHandler(ev, new NullContext());
            }
            catch (Exception)
            {
                // Fail-safe: keep original text on hook error.
                continue;
            }

            var result = ev.Result;
            if (result is null) continue;

            if (result.Handled)
                return result;

            if (result.Text is not null)
                current = result.Text;
        }
        return new InputHookResult { Handled = false, Text = current };
    }

    /// <summary>Minimal context for hook handlers that don't need full IPhiContext (hooks only
    /// observe tool call args; the extension's real context is available via api.Context).</summary>
    private sealed class NullContext : IPhiContext
    {
        public string Cwd => "";
        public string Model => "";
        public string ProviderName => "";
        public string SessionId => "";
        public string SystemPrompt => "";
        public bool IsRunning => false;
        public bool HasUi => false;
        public IReadOnlyList<Phi.Agent.IAgentMessage> Transcript => [];
        public IPhiUiBridge Ui => new NullPhiUiBridge();
    }
}
