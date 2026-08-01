using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiProvider;

/// <summary>
/// OpenAI-compatible chat completions streaming provider.
/// Supports text streaming and streamed tool calls. Reasoning content,
/// retries, and the Responses API land in later rounds.
/// </summary>
public sealed class OpenAICompatibleProvider(OpenAICompatibleConfig config, HttpClient http) : IPhiProvider
{
    public async IAsyncEnumerable<ProviderEvent> StreamResponseAsync(
        string model,
        string system,
        IList<IAgentMessage> messages,
        IReadOnlyList<Tool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
            yield return new ProviderErrorEvent($"HTTP {(int)response.StatusCode}: {errorBody}");
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var accumulatedText = new StringBuilder();
        var toolCallBuilders = new Dictionary<int, ToolCallBuilder>();
        var finishReason = StopReasons.Stop;
        string? responseModel = null;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0 || !line.StartsWith("data:")) continue;
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
                if (delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String &&
                    content.GetString() is { } text &&
                    text.Length > 0)
                {
                    accumulatedText.Append(text);
                    yield return new ProviderTextDeltaEvent(text);
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
                finishReason = reason switch
                {
                    "stop" => StopReasons.Stop,
                    "length" => StopReasons.Length,
                    "tool_calls" => StopReasons.ToolUse,
                    _ => reason,
                };
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

        var finalContent = new List<ContentBlock>();
        if (accumulatedText.Length > 0)
            finalContent.Add(new TextBlock(accumulatedText.ToString()));
        finalContent.AddRange(toolCalls);

        var finalMessage = new AssistantMessage
        {
            Api = config.Api,
            Provider = config.Provider,
            Model = responseModel ?? model,
            Content = finalContent,
            StopReason = finishReason,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        yield return new ProviderResponseEndEvent(finalMessage, finishReason);
    }

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