using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Phi.Agent;

namespace Phi.Provider;

/// <summary>
/// OpenAI-compatible chat completions streaming provider.
/// Supports text streaming and streamed tool calls. Reasoning content,
/// retries, and the Responses API land in later rounds.
/// </summary>
public sealed class OpenAICompatibleProvider(OpenAICompatibleConfig config, HttpClient http) : IPhiProvider
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

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/chat/completions"))
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

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
        var toolCallBuilders = new Dictionary<int, ToolCallBuilder>();
        string? responseModel = null;
        string? wireFinishReason = null;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            JsonElement chunk;
            try
            {
                chunk = JsonDocument.Parse(data).RootElement;
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk.TryGetProperty("model", out var m) && m.GetString() is { } serverModel)
                responseModel = serverModel;

            if (!chunk.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var choice = choices[0];

            if (choice.TryGetProperty("delta", out var delta))
            {
                // Reasoning / extended-thinking content: OpenAI-compatible
                // endpoints surface this via a "reasoning_content" field on
                // each delta. Translated to a ThinkingDeltaEvent so
                // the loop's canonicalizer routes it to a ThinkingBlock.
                if (delta.TryGetProperty("reasoning_content", out var reasoning) &&
                    reasoning.ValueKind == JsonValueKind.String &&
                    reasoning.GetString() is { } reasoningText &&
                    reasoningText.Length > 0)
                {
                    yield return new ThinkingDeltaEvent(reasoningText);
                }

                if (delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String &&
                    content.GetString() is { } text &&
                    text.Length > 0)
                {
                    yield return new TextDeltaEvent(text);
                }

                if (delta.TryGetProperty("tool_calls", out var toolCallsArray) &&
                    toolCallsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var toolCallDelta in toolCallsArray.EnumerateArray())
                    {
                        var index = GetToolCallIndex(toolCallDelta);
                        if (!toolCallBuilders.TryGetValue(index, out var builder))
                        {
                            builder = new ToolCallBuilder();
                            toolCallBuilders[index] = builder;
                        }
                        builder.AddDelta(toolCallDelta);
                    }
                }
            }

            if (choice.TryGetProperty("finish_reason", out var fr) &&
                fr.ValueKind == JsonValueKind.String &&
                fr.GetString() is { } reason)
            {
                wireFinishReason = reason;
            }
        }

        // Flush accumulated tool calls as discrete events so the loop can
        // apply them in order.
        foreach (var toolCall in toolCallBuilders
                     .OrderBy(kv => kv.Key)
                     .Select(kv => kv.Value.Build()))
        {
            yield return new ToolCallEvent(toolCall);
        }

        // Terminal: build the authoritative final message from the accumulated
        // text + tool calls. The loop calls AdoptFinal on this when consuming
        // the AssistantDoneEvent; partial.Content (kept streamed-order
        // by the loop) stays authoritative for Content, but StopReason /
        // Usage / Model / response metadata come from this terminal message.
        var finalContent = new List<ContentBlock>();
        // (text was emitted as deltas; here we just attach any non-delta
        // content the model produced directly. OpenAI doesn't normally send
        // terminal content without deltas, so this stays empty.)
        var finalMessage = new AssistantMessage
        {
            Api = config.Api,
            Provider = config.Provider,
            Model = responseModel ?? model,
            Content = finalContent,
            StopReason = AssistantMessageBuilder.MapFinishReason(wireFinishReason),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        yield return new AssistantDoneEvent(
            finalMessage,
            AssistantMessageBuilder.MapFinishReason(wireFinishReason));
    }

    private static bool IsEmptyFailedTurn(IAgentMessage message) =>
        message is AssistantMessage a
        && a.StopReason is StopReasons.Error or StopReasons.Aborted
        && a.Content.Count == 0;

    private static int GetToolCallIndex(JsonElement toolCallDelta)
    {
        if (toolCallDelta.TryGetProperty("index", out var indexElement) &&
            indexElement.TryGetInt32(out var index))
        {
            return index;
        }
        return 0;
    }

    private string BuildUrl(string path) =>
        $"{config.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static JsonObject BuildPayload(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools)
    {
        var messagesArray = new JsonArray();
        if (!string.IsNullOrEmpty(system))
        {
            messagesArray.Add((JsonNode)new JsonObject
            {
                ["role"] = "system",
                ["content"] = system,
            });
        }
        foreach (var msg in messages)
        {
            // Terminal failures stay in history for diagnostics, but an
            // empty error/aborted assistant turn is not model context and
            // must not be replayed (mirrors tau's _provider_context filter).
            if (IsEmptyFailedTurn(msg)) continue;
            messagesArray.Add((JsonNode)MessageToOpenAi(msg));
        }

        var payload = new JsonObject
        {
            ["model"] = model,
            ["stream"] = true,
            ["messages"] = messagesArray,
        };

        if (tools.Count > 0)
        {
            payload["tools"] = new JsonArray(tools.Select(t => new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    // DeepClone: Tool instances are reused across requests;
                    // a JsonNode can only have one parent.
                    ["parameters"] = t.Parameters.DeepClone(),
                },
            }).ToArray());
        }

        return payload;
    }

    private static JsonObject MessageToOpenAi(IAgentMessage message) => message switch
    {
        UserMessage u => new JsonObject
        {
            ["role"] = "user",
            ["content"] = u.Text,
        },
        AssistantMessage a => BuildAssistantJson(a),
        ToolResultMessage t => new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = t.ToolCallId,
            ["name"] = t.ToolName,
            ["content"] = t.Text,
        },
        // Extension-injected custom message: surface as assistant text so the
        // model sees it in context (CustomType / Details are render-only).
        CustomMessage c => new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = c.Text,
        },
        _ => throw new NotSupportedException(
            $"Message type {message.GetType().Name} not supported by the basic OpenAI compatible provider yet"),
    };

    private static JsonObject BuildAssistantJson(AssistantMessage a)
    {
        var obj = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = a.Text,
        };

        if (a.ToolCalls.Count > 0)
        {
            obj["tool_calls"] = new JsonArray(a.ToolCalls.Select(tc => new JsonObject
            {
                ["id"] = tc.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tc.Name,
                    ["arguments"] = tc.Arguments.ToJsonString(),
                },
            }).ToArray());
        }

        return obj;
    }
}
