using System.Net;
using Phi.Agent;

namespace Phi.Provider.Tests;

/// <summary>
/// Provider-internal retry behavior (mirrors tau's provider-side retry):
/// transient HTTP statuses and pre-content network failures are retried
/// with backoff; once content has streamed the failure is surfaced as a
/// <see cref="ProviderErrorEvent"/> instead of being retried (a retry
/// would duplicate the already-emitted deltas for the consumer).
/// </summary>
public class ProviderRetryTests
{
    private const string OpenAiSuccessSse = """
        data: {"id":"x","choices":[{"index":0,"delta":{"content":"hello"},"finish_reason":null}]}

        data: {"id":"x","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

        data: [DONE]

        """;

    private const string AnthropicSuccessSse = """
        data: {"type":"message_start","message":{"usage":{"input_tokens":1,"output_tokens":0}}}

        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"hello"}}

        data: {"type":"content_block_stop","index":0}

        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"}}

        data: {"type":"message_stop"}

        """;

    private static OpenAICompatibleProvider CreateOpenAi(HttpMessageHandler handler) =>
        new(
            new OpenAICompatibleConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.deepseek.com",
                Provider = "deepseek",
                // No artificial backoff in tests.
                MaxRetryDelay = TimeSpan.Zero,
            },
            new HttpClient(handler));

    private static AnthropicProvider CreateAnthropic(HttpMessageHandler handler) =>
        new(
            new AnthropicConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.anthropic.com",
                Provider = "anthropic",
                MaxRetryDelay = TimeSpan.Zero,
            },
            new HttpClient(handler));

    private static async Task<List<ProviderEvent>> CollectEvents(
        IAsyncEnumerable<ProviderEvent> source)
    {
        var list = new List<ProviderEvent>();
        await foreach (var ev in source) list.Add(ev);
        return list;
    }

    private static Task<List<ProviderEvent>> Run(IPhiProvider provider) =>
        CollectEvents(provider.StreamResponseAsync(
            model: "m", system: "", messages: [new UserMessage { Content = "hi" }], tools: []));

    [Test]
    public async Task OpenAi_TransientStatus_ThenSuccess_Retries()
    {
        var handler = new SequenceHttpHandler(
            SequenceHttpHandler.Status(HttpStatusCode.TooManyRequests, "rate limited"),
            SequenceHttpHandler.Sse(OpenAiSuccessSse));
        var provider = CreateOpenAi(handler);

        var events = await Run(provider);

        var end = events.OfType<ProviderResponseEndEvent>().Single();
        await Assert.That(end.Message.Text).IsEqualTo("hello");
        await Assert.That(events.OfType<ProviderErrorEvent>()).IsEmpty();
        await Assert.That(handler.RequestCount).IsEqualTo(2);
    }

    [Test]
    public async Task OpenAi_TransientStatus_ExhaustsRetries_SurfacesErrorEvent()
    {
        var handler = new SequenceHttpHandler(
            SequenceHttpHandler.Status(HttpStatusCode.TooManyRequests, "rate limited"),
            SequenceHttpHandler.Status(HttpStatusCode.TooManyRequests, "rate limited"),
            SequenceHttpHandler.Status(HttpStatusCode.TooManyRequests, "rate limited"));
        var provider = CreateOpenAi(handler);

        var events = await Run(provider);

        // Default MaxRetries=2 → 3 attempts total, then the error surfaces.
        await Assert.That(handler.RequestCount).IsEqualTo(3);
        var error = events.OfType<ProviderErrorEvent>().Single();
        await Assert.That(error.HttpStatus).IsEqualTo(429);
        await Assert.That(error.Message).Contains("429");
        await Assert.That(events.OfType<ProviderResponseEndEvent>()).IsEmpty();
    }

    [Test]
    public async Task OpenAi_NonTransientStatus_NotRetried()
    {
        var handler = new SequenceHttpHandler(
            SequenceHttpHandler.Status(HttpStatusCode.Unauthorized, "invalid key"),
            SequenceHttpHandler.Sse(OpenAiSuccessSse));
        var provider = CreateOpenAi(handler);

        var events = await Run(provider);

        await Assert.That(handler.RequestCount).IsEqualTo(1);
        var error = events.OfType<ProviderErrorEvent>().Single();
        await Assert.That(error.HttpStatus).IsEqualTo(401);
    }

    [Test]
    public async Task OpenAi_ServerErrorStatus_Retried()
    {
        var handler = new SequenceHttpHandler(
            SequenceHttpHandler.Status(HttpStatusCode.InternalServerError, "boom"),
            SequenceHttpHandler.Sse(OpenAiSuccessSse));
        var provider = CreateOpenAi(handler);

        var events = await Run(provider);

        await Assert.That(events.OfType<ProviderResponseEndEvent>().Count()).IsEqualTo(1);
        await Assert.That(handler.RequestCount).IsEqualTo(2);
    }

    [Test]
    public async Task OpenAi_NetworkError_BeforeAnyContent_Retries()
    {
        var handler = new SequenceHttpHandler(
            SequenceHttpHandler.Throw(new HttpRequestException("connection reset")),
            SequenceHttpHandler.Sse(OpenAiSuccessSse));
        var provider = CreateOpenAi(handler);

        var events = await Run(provider);

        var end = events.OfType<ProviderResponseEndEvent>().Single();
        await Assert.That(end.Message.Text).IsEqualTo("hello");
        await Assert.That(handler.RequestCount).IsEqualTo(2);
    }

    [Test]
    public async Task OpenAi_Timeout_BeforeAnyContent_Retries()
    {
        // HttpClient.Timeout surfaces as TaskCanceledException whose token is
        // not the caller's — treated as a retryable network failure.
        var handler = new SequenceHttpHandler(
            SequenceHttpHandler.Throw(new TaskCanceledException("timeout")),
            SequenceHttpHandler.Sse(OpenAiSuccessSse));
        var provider = CreateOpenAi(handler);

        var events = await Run(provider);

        await Assert.That(events.OfType<ProviderResponseEndEvent>().Count()).IsEqualTo(1);
        await Assert.That(handler.RequestCount).IsEqualTo(2);
    }

    [Test]
    public async Task OpenAi_MidStreamFailure_AfterContentEmitted_NotRetried()
    {
        // One full SSE line streams (a text delta reaches the consumer),
        // then the connection drops. Retrying would duplicate the emitted
        // delta, so the failure surfaces as an error event instead.
        var handler = new SequenceHttpHandler(
            SequenceHttpHandler.SseThenFail(
                "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n"),
            SequenceHttpHandler.Sse(OpenAiSuccessSse));
        var provider = CreateOpenAi(handler);

        var events = await Run(provider);

        await Assert.That(handler.RequestCount).IsEqualTo(1);
        var text = string.Concat(events.OfType<ProviderTextDeltaEvent>().Select(e => e.Delta));
        await Assert.That(text).IsEqualTo("partial");
        var error = events.OfType<ProviderErrorEvent>().Single();
        await Assert.That(error.Message).Contains("connection drop");
        await Assert.That(events.OfType<ProviderResponseEndEvent>()).IsEmpty();
    }

    [Test]
    public async Task Anthropic_TransientStatus_ThenSuccess_Retries()
    {
        var handler = new SequenceHttpHandler(
            SequenceHttpHandler.Status(HttpStatusCode.ServiceUnavailable, "overloaded"),
            SequenceHttpHandler.Sse(AnthropicSuccessSse));
        var provider = CreateAnthropic(handler);

        var events = await Run(provider);

        var end = events.OfType<ProviderResponseEndEvent>().Single();
        await Assert.That(end.Message.Text).IsEqualTo("hello");
        await Assert.That(handler.RequestCount).IsEqualTo(2);
    }

    [Test]
    public async Task Anthropic_NetworkError_BeforeAnyContent_Retries()
    {
        var handler = new SequenceHttpHandler(
            SequenceHttpHandler.Throw(new HttpRequestException("connection reset")),
            SequenceHttpHandler.Sse(AnthropicSuccessSse));
        var provider = CreateAnthropic(handler);

        var events = await Run(provider);

        await Assert.That(events.OfType<ProviderResponseEndEvent>().Count()).IsEqualTo(1);
        await Assert.That(handler.RequestCount).IsEqualTo(2);
    }
}
