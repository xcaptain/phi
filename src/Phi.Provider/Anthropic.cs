using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Phi.Agent;

namespace Phi.Provider;

/// <summary>
/// Anthropic Messages API provider. Covers the official Anthropic API plus
/// any re-publisher that speaks the same wire format. Streams text, tool
/// calls, and extended-thinking blocks. Retries, OAuth, and adaptive thinking
/// land in later rounds.
/// </summary>
public sealed class AnthropicProvider(AnthropicConfig config, HttpClient http) : IPhiProvider
{
    private int _disposed;

    /// <summary>
    /// Releases the owned <see cref="HttpClient"/>. Idempotent; safe to call
    /// when the provider is replaced at runtime.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        http.Dispose();
    }

    public IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        CancellationToken cancellationToken = default) =>
        ProviderRetry.WithRetriesAsync(
            ct => StreamOnceAsync(model, system, messages, tools, ct),
            config.MaxRetries,
            config.MaxRetryDelay,
            cancellationToken);

    private async IAsyncEnumerable<ProviderEvent> StreamOnceAsync(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var payload = BuildPayload(model, system, messages, tools);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/v1/messages"))
        {
            // JsonContent.Create(T, options) uses reflection-based STJ; the
            // node-based ToJsonString() is NativeAOT-safe.
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (config.BearerAuth)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }
        else
        {
            request.Headers.Add("x-api-key", config.ApiKey);
        }
        request.Headers.Add("anthropic-version", config.AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Network failures (HttpRequestException) and HttpClient timeouts
        // propagate to the retry driver in StreamResponseAsync, which
        // retries pre-content failures and converts the rest into
        // AssistantErrorEvent.
        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            yield return new AssistantErrorEvent(
                $"HTTP {(int)response.StatusCode}: {errorBody}")
            {
                HttpStatus = (int)response.StatusCode,
            };
            yield break;
        }

        // Pi-compatible begin marker. The loop already synthesizes
        // MessageStartEvent before driving this stream, so this is a
        // no-op at the agent layer — it's here so projectors / extensions
        // can observe an explicit begin signal from the provider.
        yield return new AssistantStartEvent();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // The provider is intentionally dumb: it just translates wire format
        // into typed ProviderEvents and yields them. The agent loop (and
        // CompactionSummarizer) build the running AssistantMessage partial by
        // passing each event through AssistantMessageBuilder.Apply. This
        // mirrors tau: providers yield raw AssistantMessageEvents; the loop
        // accumulates via canonicalize_provider_stream + _assistant_events.
        //
        // Anthropic's wire format has a quirk: content_block_start(type=thinking)
        // and content_block_start(type=text) both exist but carry no payload —
        // the canonicalizer opens thinking/text blocks lazily on the first
        // delta of that block kind. Only tool_use needs upfront id/name from
        // content_block_start. We also track per-index signature fragments
        // internally because Anthropic sends signature_delta as a separate
        // event kind (vs OpenAI, which folds signature into the thinking
        // block's text); the consolidated value surfaces on the
        // ThinkingEndEvent so the public protocol never carries a separate
        // signature fragment event.
        var toolCallBuilders = new Dictionary<int, AnthropicToolCallBuilder>();
        // Indices that have received at least one thinking_delta — the set
        // of open thinking blocks. content_block_stop for one of these
        // emits ThinkingEndEvent (with or without a signature).
        var seenThinkingDelta = new HashSet<int>();
        var thinkingSignatures = new Dictionary<int, string>();
        Usage usage = new();
        string? wireStopReason = null;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            // Anthropic SSE: blank line separates events; each event has
            // optional "event:" + "data:" lines. We only care about data:.
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;

            JsonElement chunk;
            try
            {
                chunk = JsonDocument.Parse(data).RootElement;
            }
            catch (JsonException)
            {
                continue;
            }

            if (!chunk.TryGetProperty("type", out var eventType) ||
                eventType.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            switch (eventType.GetString())
            {
                case "message_start":
                    if (chunk.TryGetProperty("message", out var message))
                        usage = ApplyMessageStartUsage(message.GetProperty("usage"), usage);
                    break;

                case "content_block_start":
                    {
                        var startIndex = GetIndex(chunk);
                        if (chunk.TryGetProperty("content_block", out var startBlock) &&
                            startBlock.ValueKind == JsonValueKind.Object &&
                            startBlock.TryGetProperty("type", out var startType) &&
                            startType.ValueKind == JsonValueKind.String &&
                            startType.GetString() == "tool_use")
                        {
                            // Only tool_use needs upfront setup (id/name land in
                            // content_block_start; arg JSON streams in via deltas).
                            // Thinking/text blocks open lazily on first delta.
                            if (!toolCallBuilders.TryGetValue(startIndex, out var tb))
                            {
                                tb = new AnthropicToolCallBuilder();
                                toolCallBuilders[startIndex] = tb;
                            }
                            if (startBlock.TryGetProperty("id", out var id) &&
                                id.ValueKind == JsonValueKind.String)
                                tb.Id = id.GetString();
                            if (startBlock.TryGetProperty("name", out var name) &&
                                name.ValueKind == JsonValueKind.String)
                                tb.Name = name.GetString();
                        }
                        break;
                    }

                case "content_block_delta":
                    {
                        var deltaIndex = GetIndex(chunk);
                        var (deltaKind, deltaPayload) = ExtractContentBlockDelta(chunk);
                        switch (deltaKind, deltaPayload)
                        {
                            case (ContentBlockDeltaKind.Text, { } text):
                                yield return new TextDeltaEvent(text);
                                break;
                            case (ContentBlockDeltaKind.Thinking, { } thinkingText):
                                // Lazy-open the thinking block on the first
                                // thinking_delta for this index, mirroring tau's
                                // canonicalize_provider_stream. The block's
                                // existence is implied — there's no separate
                                // ThinkingStartEvent to emit.
                                seenThinkingDelta.Add(deltaIndex);
                                yield return new ThinkingDeltaEvent(thinkingText);
                                break;
                            case (ContentBlockDeltaKind.Signature, { } sig):
                                // Signature fragments land on a separate delta
                                // stream. The signature is adapter-internal
                                // state — accumulate per-index and surface the
                                // consolidated value on the ThinkingEndEvent so
                                // the public protocol stays clean. No
                                // ThinkingSignatureEvent leak.
                                thinkingSignatures[deltaIndex] =
                                    (thinkingSignatures.TryGetValue(deltaIndex, out var prev) ? prev : "")
                                    + sig;
                                break;
                            case (ContentBlockDeltaKind.InputJson, { } fragment):
                                if (!toolCallBuilders.TryGetValue(deltaIndex, out var builder))
                                {
                                    builder = new AnthropicToolCallBuilder();
                                    toolCallBuilders[deltaIndex] = builder;
                                }
                                builder.AppendArguments(fragment);
                                break;
                        }
                        break;
                    }

                case "content_block_stop":
                    {
                        var stopIndex = GetIndex(chunk);
                        if (toolCallBuilders.TryGetValue(stopIndex, out var tcb))
                        {
                            yield return new ToolCallEvent(tcb.Build());
                            toolCallBuilders.Remove(stopIndex);
                        }
                        else if (seenThinkingDelta.Remove(stopIndex))
                        {
                            // Close the thinking block at this index — every
                            // block that streamed a delta gets an end event,
                            // signature or not. The signature (when Anthropic
                            // sent signature_delta fragments) rides on the
                            // end event so consumers see the consolidated
                            // state on a single ThinkingEndEvent.
                            var sig = thinkingSignatures.TryGetValue(stopIndex, out var s)
                                ? s : null;
                            yield return new ThinkingEndEvent(
                                new ThinkingBlock("") { ThinkingSignature = sig });
                        }
                        break;
                    }

                case "message_delta":
                    if (chunk.TryGetProperty("delta", out var msgDelta) &&
                        msgDelta.TryGetProperty("stop_reason", out var sr) &&
                        sr.ValueKind == JsonValueKind.String &&
                        sr.GetString() is { } reason)
                    {
                        wireStopReason = reason;
                    }
                    if (chunk.TryGetProperty("usage", out var deltaUsage))
                        usage = ApplyMessageDeltaUsage(deltaUsage, usage);
                    break;

                case "error":
                    yield return new AssistantErrorEvent(
                        ExtractErrorMessage(chunk));
                    yield break;
            }
        }

        // Surface any tool-call builders that never received a
        // content_block_stop (truncated stream).
        foreach (var (_, tcb) in toolCallBuilders.OrderBy(kv => kv.Key).ToList())
        {
            yield return new ToolCallEvent(tcb.Build());
        }

        var finalMessage = new AssistantMessage
        {
            Api = config.Api,
            Provider = config.Provider,
            Model = model,
            Content = [],   // loop keeps streamed-order Content from partial; AdoptFinal copies stop_reason/usage/model/response metadata only
            StopReason = AssistantMessageBuilder.MapFinishReason(MapAnthropicStopReason(wireStopReason)),
            Usage = usage,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        yield return new AssistantDoneEvent(
            finalMessage, finalMessage.StopReason);
    }

    private enum ContentBlockDeltaKind { None, Text, Thinking, Signature, InputJson }

    /// <summary>
    /// Parses one <c>content_block_delta</c> chunk into a (kind, payload)
    /// pair. Payload is the relevant string field (text / thinking text /
    /// signature / input_json fragment); null when the chunk shape isn't
    /// recognized.
    /// </summary>
    private static (ContentBlockDeltaKind Kind, string? Payload) ExtractContentBlockDelta(JsonElement chunk)
    {
        if (!chunk.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
            return (ContentBlockDeltaKind.None, null);
        if (!delta.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String)
            return (ContentBlockDeltaKind.None, null);

        switch (t.GetString())
        {
            case "text_delta":
                if (delta.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    return (ContentBlockDeltaKind.Text, text.GetString());
                return (ContentBlockDeltaKind.Text, null);
            case "thinking_delta":
                if (delta.TryGetProperty("thinking", out var thinking) && thinking.ValueKind == JsonValueKind.String)
                    return (ContentBlockDeltaKind.Thinking, thinking.GetString());
                return (ContentBlockDeltaKind.Thinking, null);
            case "signature_delta":
                if (delta.TryGetProperty("signature", out var sig) && sig.ValueKind == JsonValueKind.String)
                    return (ContentBlockDeltaKind.Signature, sig.GetString());
                return (ContentBlockDeltaKind.Signature, null);
            case "input_json_delta":
                if (delta.TryGetProperty("partial_json", out var pj) && pj.ValueKind == JsonValueKind.String)
                    return (ContentBlockDeltaKind.InputJson, pj.GetString());
                return (ContentBlockDeltaKind.InputJson, null);
            default:
                return (ContentBlockDeltaKind.None, null);
        }
    }

    private static int GetIndex(JsonElement chunk)
    {
        if (chunk.TryGetProperty("index", out var idx) &&
            idx.TryGetInt32(out var i)) return i;
        return 0;
    }

    /// <summary>
    /// Returns the trailing <see cref="ThinkingBlock"/> on
    /// <paramref name="partial"/>, or an empty block if the last block is
    /// something else. Anthropic only closes thinking blocks via
    /// <c>content_block_stop</c> immediately after the trailing
    /// ThinkingBlock, so the partial's last block at that point is the one
    /// we want.
    /// </summary>
    private static ThinkingBlock ExtractLastThinkingBlock(AssistantMessage partial)
    {
        if (partial.Content.Count > 0 && partial.Content[^1] is ThinkingBlock tb)
            return tb;
        return new ThinkingBlock("");
    }

    /// <summary>
    /// Maps an Anthropic wire stop_reason string to the canonical OpenAI
    /// vocabulary that <see cref="AssistantMessageBuilder.MapFinishReason"/>
    /// understands ("stop" / "tool_use" / "length"). Anthropic-specific
    /// values like <c>end_turn</c> collapse to <c>stop</c>; unknown values
    /// pass through unchanged.
    /// </summary>
    private static string MapAnthropicStopReason(string? reason) => reason switch
    {
        "end_turn" or "stop_sequence" => "stop",
        "tool_use" => "tool_use",
        "max_tokens" => "length",
        null => "stop",
        _ => reason,
    };

    private static Usage ApplyMessageStartUsage(JsonElement raw, Usage previous)
    {
        if (raw.ValueKind != JsonValueKind.Object) return previous;
        var input = TryGetInt(raw, "input_tokens") ?? previous.Input;
        var output = TryGetInt(raw, "output_tokens") ?? previous.Output;
        var cacheRead = TryGetInt(raw, "cache_read_input_tokens") ?? previous.CacheRead;
        var cacheWrite = TryGetInt(raw, "cache_creation_input_tokens") ?? previous.CacheWrite;
        return new Usage
        {
            Input = input,
            Output = output,
            CacheRead = cacheRead,
            CacheWrite = cacheWrite,
            TotalTokens = input + output + cacheRead + cacheWrite,
        };
    }

    private static Usage ApplyMessageDeltaUsage(JsonElement raw, Usage previous)
    {
        if (raw.ValueKind != JsonValueKind.Object) return previous;
        // message_delta only reports output_tokens; leave input/cache_* alone.
        var output = TryGetInt(raw, "output_tokens") ?? previous.Output;
        var reasoning = previous.Reasoning;
        if (raw.TryGetProperty("output_tokens_details", out var details) &&
            details.ValueKind == JsonValueKind.Object &&
            TryGetInt(details, "thinking_tokens") is { } thinking)
        {
            reasoning = thinking;
        }
        return new Usage
        {
            Input = previous.Input,
            Output = output,
            CacheRead = previous.CacheRead,
            CacheWrite = previous.CacheWrite,
            CacheWrite1h = previous.CacheWrite1h,
            Reasoning = reasoning,
            TotalTokens = previous.Input + output + previous.CacheRead + previous.CacheWrite,
        };
    }

    private static int? TryGetInt(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.Number) return null;
        if (!prop.TryGetInt32(out var v)) return null;
        return v;
    }

    private static string ExtractErrorMessage(JsonElement chunk)
    {
        if (chunk.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("message", out var msg) &&
            msg.ValueKind == JsonValueKind.String)
        {
            return msg.GetString() ?? "Provider returned an error";
        }
        return "Provider returned an error";
    }

    private string BuildUrl(string path) =>
        $"{config.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private JsonObject BuildPayload(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = config.MaxTokens,
            ["stream"] = true,
        };

        if (!string.IsNullOrEmpty(system))
        {
            payload["system"] = system;
        }

        payload["messages"] = BuildMessages(messages);

        if (tools.Count > 0)
        {
            payload["tools"] = new JsonArray(tools.Select(t => new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                // input_schema (Anthropic) vs parameters (OpenAI).
                // DeepClone: Tool instances are reused across requests;
                // a JsonNode can only have one parent.
                ["input_schema"] = t.Parameters.DeepClone(),
            }).ToArray());
        }

        return payload;
    }

    private static bool IsEmptyFailedTurn(IAgentMessage message) =>
        message is AssistantMessage a
        && a.StopReason is StopReasons.Error or StopReasons.Aborted
        && a.Content.Count == 0;

    private static JsonObject MessageToAnthropic(IAgentMessage message) => message switch
    {
        UserMessage u => new JsonObject
        {
            ["role"] = "user",
            ["content"] = ExtractUserContent(u),
        },
        AssistantMessage a => BuildAssistantJson(a),
        ToolResultMessage t => new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = t.ToolCallId,
                ["content"] = t.Text,
                ["is_error"] = t.IsError,
            }),
        },
        // Extension-injected custom message: surface as assistant text so the
        // model sees it in context (CustomType / Details are render-only).
        CustomMessage c => new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = c.Text,
            }),
        },
        _ => throw new NotSupportedException(
            $"Message type {message.GetType().Name} not supported by the Anthropic provider yet"),
    };

    /// <summary>
    /// Builds the messages JSON array. Consecutive <see cref="ToolResultMessage"/>
    /// entries are merged into a single user message with multiple
    /// <c>tool_result</c> content blocks. DeepSeek's Anthropic-compatible
    /// endpoint requires ALL tool results for one assistant turn to be in
    /// a single user message immediately after the assistant message.
    /// </summary>
    private static JsonArray BuildMessages(IList<IAgentMessage> messages)
    {
        var result = new JsonArray();
        var i = 0;
        while (i < messages.Count)
        {
            // Terminal failures stay in history for diagnostics, but an
            // empty error/aborted assistant turn is not model context —
            // Anthropic rejects empty content arrays (mirrors tau's
            // _provider_context filter).
            if (IsEmptyFailedTurn(messages[i]))
            {
                i++;
                continue;
            }
            if (messages[i] is ToolResultMessage)
            {
                var blocks = new JsonArray();
                while (i < messages.Count && messages[i] is ToolResultMessage tr)
                {
                    blocks.Add((JsonNode)new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = tr.ToolCallId,
                        ["content"] = tr.Text,
                        ["is_error"] = tr.IsError,
                    });
                    i++;
                }
                result.Add((JsonNode)new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = blocks,
                });
            }
            else
            {
                result.Add((JsonNode)MessageToAnthropic(messages[i]));
                i++;
            }
        }
        return result;
    }

    private static string ExtractUserContent(UserMessage u)
    {
        // Phi Agent currently treats UserMessage.Content as plain text; the
        // Anthropic provider passes it as a string content block. Multi-block
        // user content lands when Phi Agent grows image/file support.
        return u.Text;
    }

    private static JsonObject BuildAssistantJson(AssistantMessage a)
    {
        var content = new JsonArray();

        foreach (var block in a.Content)
        {
            switch (block)
            {
                case TextBlock tb:
                    content.Add((JsonNode)new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = tb.Text,
                    });
                    break;
                case ThinkingBlock thb:
                    var thinking = new JsonObject
                    {
                        ["type"] = "thinking",
                        ["thinking"] = thb.Thinking,
                    };
                    if (thb.ThinkingSignature is not null)
                        thinking["signature"] = thb.ThinkingSignature;
                    content.Add((JsonNode)thinking);
                    break;
                case ToolCall tc:
                    content.Add((JsonNode)new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = tc.Id,
                        ["name"] = tc.Name,
                        ["input"] = tc.Arguments.DeepClone(),
                    });
                    break;
                    // ImageBlock / unknown blocks: skip for now; the basic
                    // Anthropic provider is text + tool_use + thinking only.
            }
        }

        return new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = content,
        };
    }
}

/// <summary>
/// Accumulates streamed Anthropic tool_use deltas into a single <see cref="ToolCall"/>.
/// Anthropic sends <c>content_block_delta.input_json_delta.partial_json</c> as
/// JSON fragments that have to be concatenated across chunks before parsing.
/// </summary>
internal sealed class AnthropicToolCallBuilder
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    private readonly StringBuilder _argumentsBuffer = new();

    public void AppendArguments(string fragment)
    {
        if (fragment.Length > 0) _argumentsBuffer.Append(fragment);
    }

    public ToolCall Build() => new(Id ?? "", Name ?? "")
    {
        Arguments = ParseArguments(_argumentsBuffer.ToString()),
    };

    private static JsonObject ParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var node = JsonNode.Parse(json);
            return node as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
