using System.Runtime.CompilerServices;
using Phi.Agent;

namespace Phi.Provider;

/// <summary>
/// Provider-internal retry driver (mirrors tau's provider-side retry
/// envelope). Wraps a single-attempt stream and retries the request when:
/// <list type="bullet">
/// <item>the HTTP status is transient (408/409/425/429/5xx), or</item>
/// <item>the request fails with a network error or non-user cancellation
/// (e.g. <c>HttpClient.Timeout</c>) <b>before any content was emitted</b>.</item>
/// </list>
/// Once content (text/thinking/tool-call events) has streamed to the
/// consumer a retry would duplicate it, so mid-stream failures surface as a
/// <see cref="AssistantErrorEvent"/> instead. Retries are transparent to the
/// agent loop: it only ever sees one logical stream.
/// </summary>
internal static class ProviderRetry
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Transient HTTP statuses worth retrying (matches tau's _is_transient_status).</summary>
    public static bool IsTransientStatus(int status) =>
        status is 408 or 409 or 425 or 429 || status >= 500;

    /// <summary>Exponential backoff capped by <paramref name="maxRetryDelay"/>;
    /// a non-positive cap means no delay (used by tests).</summary>
    public static TimeSpan Backoff(int attempt, TimeSpan maxRetryDelay)
    {
        if (maxRetryDelay <= TimeSpan.Zero) return TimeSpan.Zero;
        var baseDelay = BaseDelay < maxRetryDelay ? BaseDelay : maxRetryDelay;
        var delay = baseDelay * (1 << attempt);
        return delay > maxRetryDelay ? maxRetryDelay : delay;
    }

    /// <summary>
    /// Drives <paramref name="streamOnce"/> with up to
    /// <paramref name="maxRetries"/> additional attempts. A fresh iterator is
    /// created per attempt, so each retry resends the full request.
    /// </summary>
    public static async IAsyncEnumerable<ProviderEvent> WithRetriesAsync(
        Func<CancellationToken, IAsyncEnumerable<ProviderEvent>> streamOnce,
        int maxRetries,
        TimeSpan maxRetryDelay,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var emittedContent = false;
            var retryAfterStatusError = false;
            Exception? networkFailure = null;

            var enumerator = streamOnce(cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // HttpClient.Timeout and friends: the caller's token
                        // didn't fire, so this is a provider-side failure.
                        networkFailure = new TimeoutException("The provider request timed out.");
                        break;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException)
                    {
                        networkFailure = ex;
                        break;
                    }

                    if (!hasNext) break;

                    var ev = enumerator.Current;
                    switch (ev)
                    {
                        case TextDeltaEvent
                            or ThinkingDeltaEvent
                            or ThinkingEndEvent
                            or ToolCallEvent:
                            // Any of these means the model has produced
                            // content for this attempt — a mid-stream retry
                            // would duplicate already-emitted deltas.
                            // (AssistantStartEvent is also a content signal
                            // upstream; the agent loop filters it to a
                            // no-op so it doesn't reach here.)
                            emittedContent = true;
                            break;
                        case AssistantErrorEvent err
                            when err.HttpStatus is { } status
                                && IsTransientStatus(status)
                                && attempt < maxRetries:
                            // HTTP failures arrive before any content, so a
                            // retry is always clean. Swallow the event and
                            // start a fresh attempt.
                            retryAfterStatusError = true;
                            break;
                    }

                    if (retryAfterStatusError) break;
                    yield return ev;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            if (retryAfterStatusError)
            {
                await DelayBeforeRetryAsync(attempt, maxRetryDelay, cancellationToken);
                continue;
            }

            if (networkFailure is not null)
            {
                if (!emittedContent && attempt < maxRetries)
                {
                    await DelayBeforeRetryAsync(attempt, maxRetryDelay, cancellationToken);
                    continue;
                }
                yield return new AssistantErrorEvent(
                    $"Network error: {networkFailure.Message}");
            }

            yield break;
        }
    }

    private static async Task DelayBeforeRetryAsync(
        int attempt, TimeSpan maxRetryDelay, CancellationToken cancellationToken)
    {
        var delay = Backoff(attempt, maxRetryDelay);
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
    }
}
