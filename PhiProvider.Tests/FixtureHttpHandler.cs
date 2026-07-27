using System.Net;
using System.Net.Http.Headers;

namespace PhiProvider.Tests;

/// <summary>
/// Test double that replays a recorded SSE fixture as if it were the real
/// provider response. Captures the request URI and body so tests can also
/// assert what the provider sent on the wire.
/// </summary>
public sealed class FixtureHttpHandler : HttpMessageHandler
{
    private readonly string _fixturePath;

    public FixtureHttpHandler(string fixturePath) => _fixturePath = fixturePath;

    public string? LastRequestUri { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri?.ToString();
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var bytes = await File.ReadAllBytesAsync(_fixturePath, cancellationToken);
        var stream = new MemoryStream(bytes);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return response;
    }
}

/// <summary>
/// Same as <see cref="FixtureHttpHandler"/> but takes the SSE body inline
/// instead of from a file. Handy for edge-case responses that don't deserve
/// their own fixture file.
/// </summary>
public sealed class InlineSseHandler : HttpMessageHandler
{
    private readonly string _sseBody;

    public InlineSseHandler(string sseBody) => _sseBody = sseBody;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_sseBody));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return Task.FromResult(response);
    }
}