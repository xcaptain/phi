namespace Phi.Agent;

/// <summary>
/// Provider-side canonicalizer: folds one <see cref="ProviderEvent"/>
/// into a running <see cref="AssistantMessage"/> partial. Mirrors
/// tau's <c>canonicalize_provider_stream</c> (in <c>tau_ai/stream.py</c>) —
/// a single switch statement that mutates the partial's content blocks in
/// place without exposing per-block-type lifecycle methods. Providers call
/// <see cref="Apply"/> for each granular event they emit. Terminal metadata
/// (<c>StopReason</c> / <c>Usage</c> / <c>Model</c>) folding lives in
/// <see cref="AdoptFinal"/>.
/// <para>
/// Thinking is treated as just another content block kind (a
/// <see cref="ThinkingBlock"/> inside <see cref="AssistantMessage.Content"/>).
/// The block opens lazily on the first <see cref="ThinkingDeltaEvent"/>;
/// <see cref="ThinkingEndEvent"/> carries the consolidated
/// <see cref="ThinkingBlock"/> (with any signature) — Anthropic adapters
/// accumulate <c>signature_delta</c> fragments internally and surface them
/// here, so no separate <c>ThinkingSignatureEvent</c> leaks into the public
/// protocol.
/// </para>
/// </summary>
public static class AssistantMessageBuilder
{
    /// <summary>
    /// Apply one granular provider event to the partial, returning the new
    /// partial. The trailing block's kind is tracked locally so deltas merge
    /// into the right block without the caller having to know the lifecycle.
    /// <para>Behavior:
    /// <list type="bullet">
    /// <item><see cref="TextDeltaEvent"/> → merge into trailing
    /// <see cref="TextBlock"/>, or open a fresh one.</item>
    /// <item><see cref="ThinkingDeltaEvent"/> → merge into trailing
    /// thinking block (open one if none exists).</item>
    /// <item><see cref="ThinkingEndEvent"/> → stamp the signature carried
    /// by this event (if any) onto the trailing <see cref="ThinkingBlock"/>.
    /// The block stays in content.</item>
    /// <item><see cref="ToolCallEvent"/> → append a <see cref="ToolCall"/>.</item>
    /// <item><see cref="AssistantStartEvent"/> and other unrecognized
    /// events (response end, error) are no-ops at this layer — handled by
    /// the agent loop / provider terminal logic.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static AssistantMessage Apply(AssistantMessage partial, ProviderEvent ev) => ev switch
    {
        TextDeltaEvent t when t.Delta.Length > 0
            => AppendText(partial, t.Delta),

        ThinkingDeltaEvent t when t.Delta.Length > 0
            => AppendThinking(partial, t.Delta),

        ThinkingEndEvent end
            => StampThinkingSignature(partial, end.Block.ThinkingSignature),

        ToolCallEvent tc
            => AppendToolCall(partial, tc.ToolCall),

        _ => partial,
    };

    /// <summary>
    /// Map an OpenAI/Anthropic wire-format finish reason string to a
    /// <see cref="StopReasons"/> constant. Unknown values fall through to
    /// <see cref="StopReasons.Stop"/>.
    /// </summary>
    public static string MapFinishReason(string? reason) => reason switch
    {
        "tool_calls" or "tool_use" or "toolUse" => StopReasons.ToolUse,
        "length" or "max_tokens" or "incomplete" => StopReasons.Length,
        _ => StopReasons.Stop,
    };

    /// <summary>
    /// Adopt the authoritative terminal metadata from the provider's final
    /// message onto the partial the canonicalizer has been maintaining.
    /// <para>
    /// <c>StopReason</c> / <c>Usage</c>, the provider identity
    /// (<c>Api</c> / <c>Provider</c>) and the server response metadata
    /// (<c>ResponseModel</c> / <c>ResponseProvider</c> / <c>ResponseId</c>)
    /// are taken from the terminal message; <c>Content</c> is NOT
    /// overwritten. The canonicalizer's streamed-order partial is the
    /// authoritative source for block ordering — the provider's terminal
    /// build is a parallel (sometimes reorder-prone: Anthropic prepends
    /// thinking, OpenAI prepends text) reconstruction, and adopting it
    /// wholesale would clobber projector state that has been tracking
    /// streamed updates. This mirrors tau's
    /// <c>canonicalize_provider_stream</c>
    /// (<c>final.api = api; final.provider = provider</c> +
    /// <c>final.content = [block.model_copy(...) for block in partial.content]</c>).
    /// </para>
    /// <para>
    /// <c>Model</c> / <c>Api</c> / <c>Provider</c> are only replaced when
    /// the terminal reports a non-empty value, so the partial's streamed /
    /// default values aren't clobbered by an empty terminal field.
    /// </para>
    /// </summary>
    public static AssistantMessage AdoptFinal(
        AssistantMessage partial,
        AssistantMessage finalMessage)
    {
        var adopted = partial with
        {
            StopReason = finalMessage.StopReason,
            Usage = finalMessage.Usage,
            ResponseModel = finalMessage.ResponseModel,
            ResponseProvider = finalMessage.ResponseProvider,
            ResponseId = finalMessage.ResponseId,
        };
        if (finalMessage.Model is { Length: > 0 } serverModel)
            adopted = adopted with { Model = serverModel };
        if (finalMessage.Api is { Length: > 0 } api)
            adopted = adopted with { Api = api };
        if (finalMessage.Provider is { Length: > 0 } provider)
            adopted = adopted with { Provider = provider };
        return adopted;
    }

    // ──────── private block-level mutators ────────

    private static AssistantMessage AppendText(AssistantMessage partial, string delta)
    {
        var blocks = partial.Content.ToList();
        if (blocks.Count > 0 && blocks[^1] is TextBlock tail)
        {
            blocks[^1] = tail with { Text = tail.Text + delta };
            return partial with { Content = blocks };
        }
        // Trailing block is not text (probably thinking just closed
        // without an explicit end event). Close it implicitly and start text.
        blocks.Add(new TextBlock(delta));
        return partial with { Content = blocks };
    }

    private static AssistantMessage AppendThinking(AssistantMessage partial, string delta)
    {
        var blocks = partial.Content.ToList();
        if (blocks.Count > 0 && blocks[^1] is ThinkingBlock tail)
        {
            blocks[^1] = tail with { Thinking = tail.Thinking + delta };
            return partial with { Content = blocks };
        }
        // First thinking delta without a prior Start event: open one.
        blocks.Add(new ThinkingBlock(delta));
        return partial with { Content = blocks };
    }

    private static AssistantMessage StampThinkingSignature(
        AssistantMessage partial, string? signature)
    {
        // Providers that embed the consolidated signature in
        // ThinkingEndEvent (Anthropic accumulates fragments and surfaces
        // them here; OpenAI doesn't separate signatures from content) land
        // here. The signature replaces any fragments the adapter had
        // previously buffered — the end event is authoritative.
        if (signature is null) return partial;
        var blocks = partial.Content.ToList();
        if (blocks.Count > 0 && blocks[^1] is ThinkingBlock tail)
        {
            blocks[^1] = tail with { ThinkingSignature = signature };
        }
        return partial with { Content = blocks };
    }

    private static AssistantMessage AppendToolCall(
        AssistantMessage partial, ToolCall call)
    {
        var blocks = partial.Content.ToList();
        blocks.Add(call);
        return partial with { Content = blocks };
    }
}
