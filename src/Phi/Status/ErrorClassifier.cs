namespace Phi.Status;

/// <summary>
/// Heuristic classifier that decides whether an error message is
/// <see cref="LooksTransient">transient</see> (network blip, rate limit, retry)
/// or persistent (auth failure, bad config, model not found). The classifier
/// is a pure function over the message text — it can be replaced or extended
/// without touching the call sites. Cases are matched case-insensitively as
/// substrings; miss-classification is safe (worst case a transient error gets
/// a transcript line, or a persistent error gets a status-bar flash).
/// </summary>
public static class ErrorClassifier
{
    private static readonly string[] TransientSignals =
    [
        "timeout",
        "timed out",
        "connection",
        "reset",
        "retry",
        "rate limit",
        "rate_limit",
        "429",
        "500",
        "502",
        "503",
        "504",
        "temporarily",
        "try again",
        "unavailable",
        "overloaded",
        "throttl",  // throttle, throttled, throttling
    ];

    public static bool LooksTransient(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        foreach (var signal in TransientSignals)
        {
            if (message.Contains(signal, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
