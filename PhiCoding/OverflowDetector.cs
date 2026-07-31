namespace PhiCoding;

/// <summary>
/// Cheap keyword probe for provider-side context-overflow errors. Matches
/// the messages Anthropic, DeepSeek, and OpenAI emit when the request
/// exceeds the model's context window. Mirrors tau's
/// <c>is_context_overflow_error</c> heuristic.
/// </summary>
public static class OverflowDetector
{
    private static readonly string[] Tokens =
    [
        "context_length_exceeded",
        "context length exceeded",
        "too many tokens",
        "context overflow",
        "maximum context length",
        "prompt is too long",
        "reduce the length",
        "context_length",
        "maximum number of tokens",
        "exceeds the model context",
    ];

    public static bool IsOverflow(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return false;
        return Tokens.Any(t => errorMessage.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}