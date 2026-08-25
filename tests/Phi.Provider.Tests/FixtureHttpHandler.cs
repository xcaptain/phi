using System.Net;
using System.Net.Http.Headers;

namespace Phi.Provider.Tests;

/// <summary>
/// Test double that replays a recorded SSE fixture as if it were the real
/// provider response. Captures the request URI and body so tests can also
/// assert what the provider sent on the wire.
/// </summary>
public sealed class FixtureHttpHandler(string fixturePath) : HttpMessageHandler
{
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

        var bytes = await File.ReadAllBytesAsync(fixturePath, cancellationToken);
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
public sealed class InlineSseHandler(string sseBody) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sseBody));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return Task.FromResult(response);
    }
}

/// <summary>
/// Test double that runs a scripted queue of steps — one per HTTP request —
/// so retry tests can script sequences like "429, then success" or
/// "connection reset, then success". Each step returns a response or throws.
/// </summary>
public sealed class SequenceHttpHandler : HttpMessageHandler
{
    public delegate Task<HttpResponseMessage> SequenceStep(
        HttpRequestMessage request, CancellationToken cancellationToken);

    private readonly Queue<SequenceStep> _steps;

    public SequenceHttpHandler(params SequenceStep[] steps) => _steps = new(steps);

    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (_steps.Count == 0)
            throw new InvalidOperationException(
                "SequenceHttpHandler ran out of scripted steps");
        return _steps.Dequeue()(request, cancellationToken);
    }

    /// <summary>A response with the given status code and plain-text body.</summary>
    public static SequenceStep Status(HttpStatusCode code, string body = "") =>
        (_, _) => Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(body),
        });

    /// <summary>A 200 OK SSE response with the given inline body.</summary>
    public static SequenceStep Sse(string sseBody) =>
        (_, _) =>
        {
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sseBody));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        };

    /// <summary>A step that fails the request with the given exception
    /// (e.g. <see cref="HttpRequestException"/> for a connection reset).</summary>
    public static SequenceStep Throw(Exception ex) =>
        (_, _) => Task.FromException<HttpResponseMessage>(ex);

    /// <summary>A 200 OK SSE response whose body yields
    /// <paramref name="prefixSse"/> and then fails mid-stream with an
    /// <see cref="IOException"/> — simulating a dropped connection after
    /// content was already streamed.</summary>
    public static SequenceStep SseThenFail(string prefixSse) =>
        (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new PartialThenFailContent(prefixSse),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        };
}

/// <summary>Content whose read stream serves a prefix then throws.</summary>
internal sealed class PartialThenFailContent(string prefix) : HttpContent
{
    protected override Task<Stream> CreateContentReadStreamAsync() =>
        Task.FromResult<Stream>(new PartialThenFailStream(prefix));

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context) =>
        await new PartialThenFailStream(prefix).CopyToAsync(stream);

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}

/// <summary>Stream that serves the prefix bytes once, then throws IOException.</summary>
internal sealed class PartialThenFailStream(string prefix) : Stream
{
    private readonly byte[] _prefix = System.Text.Encoding.UTF8.GetBytes(prefix);
    private int _offset;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _offset;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (_offset >= _prefix.Length)
            throw new IOException("Simulated connection drop mid-stream");
        var n = Math.Min(buffer.Length, _prefix.Length - _offset);
        _prefix.AsSpan(_offset, n).CopyTo(buffer.Span);
        _offset += n;
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
