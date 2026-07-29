using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiProvider;

/// <summary>
/// Anthropic Messages API provider. Covers the official Anthropic API plus
/// any re-publisher that speaks the same wire format. Streams text, tool
/// calls, and extended-thinking blocks. Retries, OAuth, and adaptive thinking
/// land in later rounds.
/// </summary>
public sealed class AnthropicProvider : IPhiProvider
{
    private readonly AnthropicConfig _config;
    private readonly HttpClient _http;

    public AnthropicProvider(AnthropicConfig config, HttpClient http)
    {
        _config = config;
        _http = http;
    }

    public async IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(model, system, messages, tools);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/v1/messages"))
        {
            Content = JsonContent.Create(payload),
        };
        if (_config.BearerAuth)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }
        else
        {
            request.Headers.Add("x-api-key", _config.ApiKey);
        }
        request.Headers.Add("anthropic-version", _config.AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // We use a flag + break to work around CS1631 (no yield in catch).
        ProviderErrorEvent? sendError = null;
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            sendError = new ProviderErrorEvent(
                $"HTTP request failed: {ex.Message}" +
                (ex.InnerException is not null ? $" ({ex.InnerException.Message})" : ""));
            yield break;
        }

        if (sendError is not null)
        {
            yield return sendError;
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            yield return new ProviderErrorEvent($"HTTP {(int)response.StatusCode}: {errorBody}");
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var accumulatedText = new StringBuilder();
        var thinkingStates = new Dictionary<int, ThinkingBlockState>();
        var completedThinkingBlocks = new SortedDictionary<int, ThinkingBlock>();
        var toolCallBuilders = new Dictionary<int, AnthropicToolCallBuilder>();
        var finishReason = StopReasons.Stop;
        Usage usage = new();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            // Anthropic SSE: blank line separates events; each event has
            // optional "event:" + "data:" lines. We only care about data:.
            if (line.Length == 0 || !line.StartsWith("data:")) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;

            JsonElement chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<JsonElement>(data);
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
                    var startIndex = GetIndex(chunk);
                    if (chunk.TryGetProperty("content_block", out var startBlock) &&
                        startBlock.ValueKind == JsonValueKind.Object &&
                        startBlock.TryGetProperty("type", out var startType) &&
                        startType.ValueKind == JsonValueKind.String)
                    {
                        switch (startType.GetString())
                        {
                            case "thinking":
                                thinkingStates[startIndex] = new ThinkingBlockState();
                                yield return new ProviderThinkingStartEvent();
                                break;
                            case "tool_use":
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
                                break;
                        }
                    }
                    break;

                case "content_block_delta":
                    var deltaIndex = GetIndex(chunk);
                    HandleContentBlockDelta(
                        chunk,
                        accumulatedText,
                        thinkingStates,
                        toolCallBuilders,
                        out var textDelta,
                        out var thinkingDelta);
                    if (textDelta is { } d)
                        yield return new ProviderTextDeltaEvent(d);
                    if (thinkingDelta is { } td)
                        yield return new ProviderThinkingDeltaEvent(td);
                    break;

                case "content_block_stop":
                    var stopIndex = GetIndex(chunk);
                    if (thinkingStates.TryGetValue(stopIndex, out var tState))
                    {
                        var block = tState.ToBlock();
                        completedThinkingBlocks[stopIndex] = block;
                        thinkingStates.Remove(stopIndex);
                        yield return new ProviderThinkingEndEvent(block);
                    }
                    break;

                case "message_delta":
                    if (chunk.TryGetProperty("delta", out var msgDelta) &&
                        msgDelta.TryGetProperty("stop_reason", out var sr) &&
                        sr.ValueKind == JsonValueKind.String &&
                        sr.GetString() is { } reason)
                    {
                        finishReason = MapStopReason(reason);
                    }
                    if (chunk.TryGetProperty("usage", out var deltaUsage))
                        usage = ApplyMessageDeltaUsage(deltaUsage, usage);
                    break;

                case "error":
                    yield return new ProviderErrorEvent(
                        ExtractErrorMessage(chunk));
                    yield break;
            }
        }

        var toolCalls = toolCallBuilders
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value.Build())
            .ToList();

        foreach (var toolCall in toolCalls)
        {
            yield return new ProviderToolCallEvent(toolCall);
        }

        // Completed thinking blocks (each already emitted a ThinkingEndEvent)
        // plus any that were never properly stopped (stream drop, missing
        // content_block_stop) — surface them too so the final message is
        // faithful even on a truncated stream.
        var finalThinkingBlocks = completedThinkingBlocks.Values.ToList();
        foreach (var (idx, state) in thinkingStates.OrderBy(kv => kv.Key))
        {
            finalThinkingBlocks.Add(state.ToBlock());
        }

        var finalContent = new List<ContentBlock>();
        finalContent.AddRange(finalThinkingBlocks);
        if (accumulatedText.Length > 0)
            finalContent.Add(new TextBlock(accumulatedText.ToString()));
        finalContent.AddRange(toolCalls);

        var finalMessage = new AssistantMessage
        {
            Api = _config.Api,
            Provider = _config.Provider,
            Model = model,
            Content = finalContent,
            StopReason = finishReason,
            Usage = usage,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        yield return new ProviderResponseEndEvent(finalMessage, finishReason);
    }

    private static int GetIndex(JsonElement chunk)
    {
        if (chunk.TryGetProperty("index", out var idx) &&
            idx.TryGetInt32(out var i)) return i;
        return 0;
    }

    private static void HandleContentBlockDelta(
        JsonElement chunk,
        StringBuilder accumulatedText,
        Dictionary<int, ThinkingBlockState> thinkingStates,
        Dictionary<int, AnthropicToolCallBuilder> toolCallBuilders,
        out string? textDelta,
        out string? thinkingDelta)
    {
        textDelta = null;
        thinkingDelta = null;

        if (!chunk.TryGetProperty("delta", out var delta) ||
            delta.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        if (!delta.TryGetProperty("type", out var deltaType) ||
            deltaType.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var index = GetIndex(chunk);

        switch (deltaType.GetString())
        {
            case "text_delta":
                if (delta.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String &&
                    text.GetString() is { Length: > 0 } t)
                {
                    accumulatedText.Append(t);
                    textDelta = t;
                }
                break;

            case "thinking_delta":
                if (!thinkingStates.TryGetValue(index, out var state))
                {
                    state = new ThinkingBlockState();
                    thinkingStates[index] = state;
                }
                if (delta.TryGetProperty("thinking", out var thinking) &&
                    thinking.ValueKind == JsonValueKind.String &&
                    thinking.GetString() is { Length: > 0 } th)
                {
                    state.Text.Append(th);
                    thinkingDelta = th;
                }
                break;

            case "signature_delta":
                if (thinkingStates.TryGetValue(index, out var sigState) &&
                    delta.TryGetProperty("signature", out var sig) &&
                    sig.ValueKind == JsonValueKind.String &&
                    sig.GetString() is { Length: > 0 } s)
                {
                    sigState.Signature.Append(s);
                }
                break;

            case "input_json_delta":
                if (!delta.TryGetProperty("partial_json", out var pj) ||
                    pj.ValueKind != JsonValueKind.String)
                {
                    return;
                }
                if (!toolCallBuilders.TryGetValue(index, out var builder))
                {
                    builder = new AnthropicToolCallBuilder();
                    toolCallBuilders[index] = builder;
                }
                builder.AppendArguments(pj.GetString() ?? "");
                break;
        }
    }

    private static string MapStopReason(string reason) => reason switch
    {
        "end_turn" => StopReasons.Stop,
        "stop_sequence" => StopReasons.Stop,
        "tool_use" => StopReasons.ToolUse,
        "max_tokens" => StopReasons.Length,
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
        $"{_config.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private JsonObject BuildPayload(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = _config.MaxTokens,
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
            if (messages[i] is ToolResultMessage)
            {
                var blocks = new JsonArray();
                while (i < messages.Count && messages[i] is ToolResultMessage tr)
                {
                    blocks.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = tr.ToolCallId,
                        ["content"] = tr.Text,
                        ["is_error"] = tr.IsError,
                    });
                    i++;
                }
                result.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = blocks,
                });
            }
            else
            {
                result.Add(MessageToAnthropic(messages[i]));
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
                    content.Add(new JsonObject
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
                    content.Add(thinking);
                    break;
                case ToolCall tc:
                    content.Add(new JsonObject
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
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try
        {
            var node = JsonNode.Parse(json);
            return node as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }
}

/// <summary>
/// Per-index state for a streamed Anthropic thinking block. Tracks the
/// accumulating text and signature across <c>thinking_delta</c> and
/// <c>signature_delta</c> events until <c>content_block_stop</c> flushes
/// the consolidated <see cref="ThinkingBlock"/>.
/// </summary>
internal sealed class ThinkingBlockState
{
    public readonly StringBuilder Text = new();
    public readonly StringBuilder Signature = new();

    public ThinkingBlock ToBlock()
    {
        var text = Text.ToString();
        var signature = Signature.Length > 0 ? Signature.ToString() : null;
        if (signature is null)
        {
            return new ThinkingBlock(text);
        }
        return new ThinkingBlock(text) { ThinkingSignature = signature };
    }
}